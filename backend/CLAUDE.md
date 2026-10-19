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
- Segredos nunca em `appsettings*.json`: use `dotnet user-secrets` ou variáveis de ambiente. Hoje: `ConnectionStrings:Cofre`, `Jwt:ChaveAssinatura` e `Criptografia:ChaveSegredos` (ver README). Testes usam chaves fictícias em `InfrastructureFixture` e `ApiFactory`: ao criar opção obrigatória nova, adicione o valor de teste nos dois.
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
- Concorrência: `cofres` ainda não tem token de concorrência; a reserva (T-0702) define o controle (UPDATE condicional e/ou `xmin`).
- A Application não referencia EF Core, ASP.NET Identity, ASP.NET Core nem bibliotecas de JWT (regras em `ArchitectureTests`). `Infrastructure.Tests` verifica o modelo EF sem banco (`IDesignTimeModel`).

### Casos de uso e portas

- Casos de uso são classes por ação (`Application/<Funcionalidade>/`, ex.: `RegistrarUsuario`, `RenovarTokens`) com `ExecutarAsync`, registradas como scoped em `AddApplication`. Devolvem um resultado tipado (ex.: `ResultadoAutenticacao` com enum de status); a Api traduz o status em HTTP. Exceção só para falha do servidor.
- Portas ficam em `Application/<Funcionalidade>/Portas/`; implementações na Infrastructure, registradas em `AddInfrastructure`. Relógio: `TimeProvider` (a Infrastructure registra `TimeProvider.System` com `TryAdd`; testes injetam um falso).
- Entidades do domínio que referenciam o usuário guardam só `UsuarioId` (Guid). A FK para `usuarios` fica no mapeamento EF.
- Escritas sujeitas a corrida (revogar, rotacionar, reservar) usam UPDATE condicional (`ExecuteUpdateAsync` com o estado esperado no `Where`, conferindo as linhas afetadas). Leituras para decisão usam `AsNoTracking`, para que alterar a entidade em memória não persista nada sem querer.

### Api

- Minimal APIs: endpoints em `Api/Endpoints/<Funcionalidade>/` com `Map<Funcionalidade>Endpoints()` sobre um `MapGroup`. Composição de auth/rate limit/Swagger em `Api/Autenticacao/AutenticacaoExtensions`.
- Corpos de requisição com campos anuláveis e validação mínima na borda devolvendo `Results.ValidationProblem` (chaves camelCase), até a T-0304 trazer FluentValidation. Erros 401/409 como `Results.Problem` com mensagem genérica em português, sem ecoar dados do usuário.
- JWT: `Infrastructure/Autenticacao/JwtOpcoes` (seção `Jwt`) é a única fonte dos parâmetros; emissão (`GeradorAccessTokenJwt`) e validação (JwtBearer, via `CriarParametrosValidacao()`) usam o mesmo objeto. `MapInboundClaims = false`: claims curtas (`sub`, `email`, `role`). A chave é validada com `ValidateOnStart`, então montar o host (`dotnet ef`) não exige a chave e subir a Api exige.
- Rate limit: políticas nomeadas `RateLimitOpcoes.PoliticaCredenciais` (registro/login) e `PoliticaSessao` (refresh/logout), particionadas por `ChaveRateLimit.Gerar` (IPv4, IPv4 mapeado normalizado, IPv6 por /64), limites em `RateLimit:AuthCredenciais` e `RateLimit:AuthSessao`.
- Contadores do Identity (falhas de login, lockout) também só com UPDATE atômico (ver `ServicoIdentidade`); entidades `Usuario` carregadas para decisão são desanexadas antes de qualquer `SaveChanges` posterior no mesmo contexto.
- `Api.Tests`: `ApiFactory` (WebApplicationFactory, ambiente `Testing`) troca só as portas que tocam o banco por fakes e mantém JWT, rate limit e endpoints reais. Rotas protegidas de teste entram por `IStartupFilter` (ver `ApiFactory`).

## Verificação

```bash
dotnet build
```

```bash
dotnet test
```
