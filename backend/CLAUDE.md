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
- Testes: `Domain.Tests` e `Application.Tests` para unidade; `ArchitectureTests` para a regra de dependência — atualize se surgir uma nova camada/proibição. Integração (Testcontainers) entra a partir da T-0307.
- Segredos nunca em `appsettings*.json`: use `dotnet user-secrets` ou variáveis de ambiente.
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
- A Application não referencia EF Core nem ASP.NET Identity (regra em `ArchitectureTests`). `Infrastructure.Tests` verifica o modelo EF sem banco (`IDesignTimeModel`).

## Verificação

```bash
dotnet build
```

```bash
dotnet test
```
