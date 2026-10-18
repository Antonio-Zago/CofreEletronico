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
- Segredos nunca em `appsettings*.json`: use `dotnet user-secrets` ou variáveis de ambiente. Hoje: `ConnectionStrings:Cofre` e `Jwt:ChaveAssinatura` (ver README).
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
