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

## Verificação

```bash
dotnet build
```

```bash
dotnet test
```
