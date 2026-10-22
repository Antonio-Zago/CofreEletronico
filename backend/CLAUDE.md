# Backend — convenções

Clean Architecture, .NET 8 (SDK fixado em `global.json`). Visão geral e comandos em [README.md](README.md).

## Onde colocar cada coisa

| Camada | Contém | Pode referenciar |
|---|---|---|
| `Domain` | Entidades (herdam `Common/Entity`), value objects, enums de status, máquinas de estado (Locacao, Cofre), exceções de domínio | nada |
| `Application` | Casos de uso, DTOs, validadores (FluentValidation), interfaces de portas (repositórios, `IMqttPublisher`, `IPagamentoGateway`, relógio) | Domain |
| `Infrastructure` | DbContext/EF Core/migrations, repositórios, MQTTnet, Mercado Pago, jobs (`IHostedService`) | Application, Domain |
| `Api` | Endpoints, auth/JWT, ProblemDetails, SignalR, `Program.cs` (composição) | Application, Infrastructure (só para DI) |

- Cada camada expõe seu registro de DI: `AddApplication()`, `AddInfrastructure(configuration)`.
- Regras de negócio ficam no Domain, não nos endpoints nem nos repositórios.
- Pastas organizadas por funcionalidade dentro da camada (ex.: `Application/Locacoes/`, `Domain/Estantes/`).
- Pacotes: adicione a versão em `Directory.Packages.props` e a referência sem `Version` no `.csproj`.
- Warnings são erros (`Directory.Build.props`).
- Testes: `Domain.Tests` e `Application.Tests` para unidade (portas com fakes); `Api.Tests` para HTTP; `ArchitectureTests` para a regra de dependência — atualize se surgir uma nova camada/proibição. Integração (Testcontainers) entra a partir da T-0307.
- Segredos nunca em `appsettings*.json`: use `dotnet user-secrets` ou variáveis de ambiente. Hoje: `ConnectionStrings:Cofre`, `Jwt:ChaveAssinatura`, `Criptografia:ChaveSegredos` e a seção opcional `AdminInicial` (ver README). Testes usam chaves fictícias em `InfrastructureFixture` e `ApiFactory`: ao criar opção obrigatória nova, adicione o valor de teste nos dois.
- Persistência: `Infrastructure/Persistencia/CofreDbContext` (Identity com chave `Guid`; `Usuario`/`Papel` em `Persistencia/Identity/`). Mapeamentos em classes `IEntityTypeConfiguration<T>` na Infrastructure, aplicadas por `ApplyConfigurationsFromAssembly`. String de conexão `ConnectionStrings:Cofre` (user-secrets ou `ConnectionStrings__Cofre`).
- `dotnet ef`: ferramenta local (`.config/dotnet-tools.json`, mesma versão do EF Core). Rode `dotnet tool restore` e use a Api como startup project:
  `dotnet ef <comando> --project src/CofreEletronico.Infrastructure --startup-project src/CofreEletronico.Api`.
  O pacote `Microsoft.EntityFrameworkCore.Design` fica só na Api, com `PrivateAssets="all"`.
- Nomes no banco: `UseSnakeCaseNamingConvention()` (EFCore.NamingConventions) + `ToTable` explícito em português. Nomes de FK/índice de tabelas do Identity ficam explícitos (a convenção os calcula antes do `ToTable`). Revise sempre o SQL (`dotnet ef migrations script`) de cada migration nova.
- Migrations em `Infrastructure/Persistencia/Migrations` (`--output-dir Persistencia/Migrations`). Seed com `HasData` usa Ids/ConcurrencyStamp fixos. Antes de concluir, `dotnet ef migrations has-pending-model-changes` deve dizer que não há mudanças.
- Identity: só `AddIdentityCore` + `AddRoles` + stores EF em `AddInfrastructure`. Política (senha, lockout, e-mail único) em código, em `Persistencia/Identity/PoliticaIdentity`, nunca em appsettings. Nomes dos papéis: `Application/Autorizacao/Papeis`.
- `Usuario`: não duplique campos que o Identity já tem; renomeie a coluna para o contrato (`PasswordHash` → `senha_hash`, `PhoneNumber` → `telefone`).
- Dados pessoais (`Nome`, `Cpf` etc.) levam `[PersonalData]`.
- Nunca use `Include Error Detail` na string de conexão nem `EnableSensitiveDataLogging()` fora da máquina local: o detalhe de uma violação de índice único expõe CPF/e-mail em log.
- Violação de unicidade do PostgreSQL (SQLSTATE `23505`) vira **409 genérico**, sem ecoar o valor conflitante.
- Datas/horas: `DateTimeOffset` em UTC (mapeia para `timestamptz`).
- Nomes explícitos para PK (`pk_<tabela>`), índices (`ix_<tabela>_<colunas>`), FKs (`fk_<tabela>_<principal>_<coluna>`) e CHECKs (`ck_<tabela>_<coluna>`), em todas as tabelas.
- Enums do domínio vão para o banco como texto em minúsculas e sem acento (`EnumTexto.Conversor<T>()`), `varchar(20)`, com CHECK gerada do próprio enum (`EnumTexto.CheckValores<T>`). Nomes dos membros dos enums nunca levam acento.
- Regras de negócio que o banco também consegue garantir (faixas, positividade, unicidade, formato do `device_id`) ficam duplicadas como CHECK/índice único, além da validação no domínio.
- Domínio: violação de regra lança `ExcecaoDominio` (`Domain/Common`); transição de estado inválida do cofre lança `TransicaoCofreInvalidaException`. Mensagens seguras para o cliente (sem segredos). Exclusão de `Estante`/`Cofre` é lógica (`Ativa`/`Ativo`); FKs para elas são `RESTRICT`.
- Máquina de estados do `Cofre` documentada na própria classe: Reservado/Ocupado não vão para Manutencao nem podem ser desativados.
- Segredos que o backend precisa ler em claro (segredo HMAC da estante) são cifrados com `ICifradorSegredos` (AES-256-GCM, `Infrastructure/Criptografia`, formato `v1:` + base64(nonce|cifrado|tag)) via `ValueConverter`. O cifrador é singleton, lê a chave só no primeiro uso (o `dotnet ef` funciona sem ela) e é injetado no `CofreDbContext`; por isso o cache de modelo é chaveado também pela instância do cifrador (`CofreModelCacheKeyFactory`). Configurações que dependem de serviço são aplicadas em `OnModelCreating` (ver `EstanteConfiguracao.ConfigurarSegredoHmac`); as demais continuam com construtor sem parâmetros. Nunca filtre consultas por coluna cifrada (o nonce é aleatório). O cifrador **tem de ser singleton** (scoped/transient vazaria um modelo por contexto; há teste). Fora do ambiente `Testing`, chaves conhecidas/triviais (a de teste versionada, bytes todos iguais) são recusadas na subida; por isso `InfrastructureFixture` registra um `IHostEnvironment` `Testing` e a `ApiFactory` sobe como `Testing`.
- Segredo em entidade nunca é propriedade pública (serializadores e logs estruturados a leriam): fica em propriedade privada mapeada pelo EF e sai só por método que devolve cópia (ex.: `Estante.ObterSegredoHmac()`).
- **Nunca devolva entidade de domínio em endpoint**: sempre DTO. O segredo HMAC só sai da Api uma vez, no cadastro ou na regeneração (T-0402).
- Concorrência: `estantes` usa a coluna de sistema `xmin` como token (shadow property `Versao`, `uint`, `xid`, `ValueGeneratedOnAddOrUpdate` + `IsConcurrencyToken`; a migration `AdicionarConcorrenciaEstantes` não gera SQL, porque o Npgsql ignora AddColumn de `xmin`). `DbUpdateConcurrencyException` no salvar vira `ResultadoSalvamento.ConflitoConcorrencia` → 409 genérico (entradas recusadas são desanexadas). `cofres` ainda não tem token; a reserva (T-0702) define o controle (UPDATE condicional e/ou `xmin`).
- A Application não referencia EF Core, ASP.NET Identity, ASP.NET Core nem bibliotecas de JWT (regras em `ArchitectureTests`). `Infrastructure.Tests` verifica o modelo EF sem banco (`IDesignTimeModel`).

### Casos de uso e portas

- Casos de uso são classes por ação (`Application/<Funcionalidade>/`, ex.: `RegistrarUsuario`, `RenovarTokens`) com `ExecutarAsync`, registradas como scoped em `AddApplication`. Devolvem um resultado tipado (ex.: `ResultadoAutenticacao` com enum de status); a Api traduz o status em HTTP. Exceção só para falha do servidor.
- Portas ficam em `Application/<Funcionalidade>/Portas/`; implementações na Infrastructure, registradas em `AddInfrastructure`. Relógio: `TimeProvider` (a Infrastructure registra `TimeProvider.System` com `TryAdd`; testes injetam um falso).
- Entidades do domínio que referenciam o usuário guardam só `UsuarioId` (Guid). A FK para `usuarios` fica no mapeamento EF.
- Aleatoriedade (device_id, segredos, senhas) fica atrás de porta (ex.: `IGeradorCredenciaisEstante`), implementada com `RandomNumberGenerator` (`GetString` para alfabetos, sem viés de módulo); os testes da Application usam gerador determinístico.
- Valor único gerado pelo servidor (ex.: `device_id`): consulta antes de inserir **e** trata o 23505 do índice (pelo `ConstraintName`) com nova tentativa limitada (`CriarEstante.MaximoTentativas`); o repositório desanexa a entidade recusada (`EntityState.Detached`) para a próxima tentativa não reenviá-la. Esgotou: status tipado → 500 genérico com log de erro.
- Leituras para exibição projetam direto para o DTO (`Select(e => new EstanteDto(...))`): não leem nem decifram coluna cifrada. Paginação e contagem no banco, com ordem estável (campo + `Id`).
- Regra que atravessa agregados (ex.: não desativar estante com cofre reservado/ocupado) fica no caso de uso, consultando o repositório.
- DTO com segredo (ex.: `CredenciaisEstanteDto`) sobrescreve `PrintMembers` para mascarar os valores no `ToString` (logs/exceções); a serialização JSON continua com os valores reais. `ArchitectureTests` garante que nenhum `*Dto` público da Application expõe tipo do Domain (exceto enums).
- Segredo em claro gerado no caso de uso (array de bytes) é zerado com `CryptographicOperations.ZeroMemory` num `finally`, também quando salvar lança exceção; o helper que monta o DTO não zera.
- Auditoria de ações admin: log informativo com o id do admin (claim `sub`), o id do recurso e o identificador público (ex.: `deviceId`); nunca segredo, senha ou e-mail.
- Escritas sujeitas a corrida (revogar, rotacionar, reservar) usam UPDATE condicional (`ExecuteUpdateAsync` com o estado esperado no `Where`, conferindo as linhas afetadas). Leituras para decisão usam `AsNoTracking`, para que alterar a entidade em memória não persista nada sem querer.

### Api

- Minimal APIs: endpoints em `Api/Endpoints/<Funcionalidade>/` com `Map<Funcionalidade>Endpoints()` sobre um `MapGroup`. Composição de auth/rate limit/Swagger em `Api/Autenticacao/AutenticacaoExtensions`.
- Corpos de requisição com campos anuláveis e validação mínima na borda devolvendo `Results.ValidationProblem` (chaves camelCase), até a T-0304 trazer FluentValidation. Erros 401/409 como `Results.Problem` com mensagem genérica em português, sem ecoar dados do usuário.
- JWT: `Infrastructure/Autenticacao/JwtOpcoes` (seção `Jwt`) é a única fonte dos parâmetros; emissão (`GeradorAccessTokenJwt`) e validação (JwtBearer, via `CriarParametrosValidacao()`) usam o mesmo objeto. `MapInboundClaims = false`: claims curtas (`sub`, `email`, `role`). A chave é validada com `ValidateOnStart`, então montar o host (`dotnet ef`) não exige a chave e subir a Api exige.
- Rate limit: políticas nomeadas `RateLimitOpcoes.PoliticaCredenciais` (registro/login) e `PoliticaSessao` (refresh/logout), particionadas por `ChaveRateLimit.Gerar` (IPv4, IPv4 mapeado normalizado, IPv6 por /64), limites em `RateLimit:AuthCredenciais` e `RateLimit:AuthSessao`. Rotas `/admin`: limite geral por administrador (`RateLimit:Admin`, 120/min) aplicado pelo `GlobalLimiter` a todo endpoint com `LimiteAdminMetadata` (posto no grupo de `MapAdminEndpoints`), e a política nomeada `RateLimitOpcoes.PoliticaAdminSensivel` (`RateLimit:AdminSensivel`, 10/min) nas rotas que geram credenciais; ambos particionados pelo `sub`. O geral é global (e não política nomeada) porque o ASP.NET Core aplica uma única política nomeada por endpoint. Opção nova de limite: valor alto na `ApiFactory` (`LimiteAdmin`, `LimiteAdminSensivel`) e entrada no `appsettings.json`.
- Autorização: FallbackPolicy = usuário autenticado. **Endpoint novo é protegido por padrão**; rota pública exige `AllowAnonymous()` explícito (hoje `/ping` e o grupo `/auth`). Políticas nomeadas em `Api/Autorizacao/Politicas` (`Politicas.Admin` = papel `Admin`), registradas em `AddAutenticacaoJwt`. Swagger fica antes de `UseAuthorization` (não passa pela fallback). 401 (com `WWW-Authenticate: Bearer`, sem detalhar o motivo) e 403 saem como ProblemDetails pelos eventos `OnChallenge`/`OnForbidden` do JwtBearer.
- **Rotas administrativas**: toda rota administrativa é registrada dentro do grupo `/admin` de `MapAdminEndpoints` (`Api/Endpoints/Admin/AdminEndpoints`), que aplica `RequireAuthorization(Politicas.Admin)` no grupo, tags e 401/403 de OpenAPI; **nunca crie rota `/admin` fora dele** nem use `AllowAnonymous()` dentro dele. Cada funcionalidade expõe um método de extensão sobre o `RouteGroupBuilder` admin, em `Api/Endpoints/Admin/<Funcionalidade>/` (ex.: `grupoAdmin.MapEstantesAdmin()`, T-0402), chamado de dentro de `MapAdminEndpoints`; ele mapeia o sub-grupo relativo (`grupoAdmin.MapGroup("/estantes")`) sem repetir a política. `RotasAdminTests` percorre o `EndpointDataSource` da Api e falha se alguma rota `/admin` não exigir a política Admin ou tiver `AllowAnonymous`, e também se alguma rota puder atender `/admin/...` sem o literal `admin` no primeiro segmento (parâmetro/catch-all/segmento composto no primeiro segmento, ou `MapFallback`), salvo lista explícita de permitidos (hoje vazia). O grupo também aplica o filtro `AdminAutenticado.ExigirSubValidoAsync` (claim `sub` com Guid válido, senão 401 + warning); handlers leem o id por `AdminAutenticado.Id(usuario)`, nunca `Guid.Empty`.
- Endpoints dentro de `MapAdminEndpoints` marcam os casos de uso com `[FromServices]`: o grupo também é montado em testes sem a DI da Api (`RotasAdminTests`), e a inferência de parâmetros falharia. O sub-grupo usa `MapPost("")`/`MapGet("")` para a raiz (o `RawText` fica `/admin/estantes/`; o roteamento aceita com e sem a barra).
- Query string com validação própria (paginação, filtros) é lida como `string?` e convertida à mão, para que valor malformado também saia como `ValidationProblem` (o binding automático devolve 400 sem corpo fora de Development). Padrões: `pagina` 1, `tamanhoPagina` 20, máximo 100.
- Resposta que contém segredo (credenciais de estante) leva `Cache-Control: no-store` e `Pragma: no-cache`. `ExcecaoDominio` que escapar do caso de uso vira 400 genérico por endpoint filter do sub-grupo, sem mensagem nem stack trace.
- Exceção não tratada: `AddProblemDetails` + `UseExceptionHandler()` no início do pipeline (fora de Development) → 500 com título/detalhe genéricos (`Api/Autenticacao/ProblemasGenericos`, só age quando `ProblemDetailsContext.Exception` existe; mantém só a extensão `traceId`). Teste em `EstantesAdminProtecaoTests` com a rota `ApiFactory.RotaExcecao`, montada depois do pipeline do Program.
- Admin inicial: caso de uso `Application/Autorizacao/GarantirAdminInicial`, opções `Infrastructure/Autorizacao/AdminInicialOpcoes` (ValidateOnStart) e chamada explícita `app.Services.GarantirAdminInicialAsync()` no `Program.cs`, em todos os ambientes, depois das migrations. Criação só via `UserManager.CreateAsync` + `AddToRolesAsync` na mesma transação, nunca `HasData`. A porta tem um método por intenção (`CriarUsuarioAsync`, `CriarAdministradorAsync`), com os papéis fixados na implementação: **nunca aceite lista de papéis vinda de quem chama**. Nunca promove conta existente e nunca altera senha. Senha recusada impede a subida; admin já existente com `AdminInicial` ainda configurada gera warning. Bloqueio por lockout de conta Admin gera warning próprio (sem e-mail).
- Logs: nunca senha; e-mail sempre mascarado com `Application/Comum/MascaraDados.MascararEmail` (ou só o `UsuarioId`).
- Contadores do Identity (falhas de login, lockout) também só com UPDATE atômico (ver `ServicoIdentidade`); entidades `Usuario` carregadas para decisão são desanexadas antes de qualquer `SaveChanges` posterior no mesmo contexto.
- `Api.Tests`: `ApiFactory` (WebApplicationFactory, ambiente `Testing`) troca só as portas que tocam o banco por fakes e mantém JWT, rate limit e endpoints reais. Rotas de teste (protegida, política Admin, sem metadado e `/admin/teste-politica` dentro do grupo de `MapAdminEndpoints`, em ramo com roteador próprio) entram por `IStartupFilter` (ver `ApiFactory`); tokens de teste com papéis saem do `IGeradorAccessToken` real da fábrica.

## Verificação

```bash
dotnet build
```

```bash
dotnet test
```
