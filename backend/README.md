# Backend

API .NET 8 do Cofre Eletrônico, em **Clean Architecture**. Detalhes do sistema em [../docs/ARQUITETURA.md](../docs/ARQUITETURA.md).

## Estrutura

```
CofreEletronico.sln
global.json                          # fixa o SDK 8.0
Directory.Build.props                # net8.0, nullable, warnings como erro
Directory.Packages.props             # versões de pacotes centralizadas
src/
  CofreEletronico.Domain             # entidades, value objects, regras e estados (sem dependências)
  CofreEletronico.Application        # casos de uso, DTOs, validação, interfaces (portas)
  CofreEletronico.Infrastructure     # implementações: EF Core, MQTT, Mercado Pago, jobs
  CofreEletronico.Api                # endpoints, auth, Swagger, SignalR; composição (DI)
tests/
  CofreEletronico.Domain.Tests
  CofreEletronico.Application.Tests
  CofreEletronico.Infrastructure.Tests  # modelo EF (sem banco), DI do Identity
  CofreEletronico.ArchitectureTests  # garante a regra de dependência (NetArchTest)
tools/EspSimulator                   # (futuro, T-0607) simulador de ESP32
```

## Regra de dependência

```
Api ──► Infrastructure ──► Application ──► Domain
 └──────────────────────────►┘
```

As dependências só apontam para dentro. A Application define interfaces (ex.: repositórios, publicador MQTT, gateway de pagamento) e a Infrastructure as implementa. Os testes em `CofreEletronico.ArchitectureTests` quebram o build de testes se a regra for violada.

## Pré-requisitos

- .NET SDK 8.0.4xx (o `global.json` seleciona a versão)

## Comandos

Dentro de `backend/`:

```bash
dotnet build
```

```bash
dotnet test
```

```bash
dotnet run --project src/CofreEletronico.Api
```

Em Development o Swagger fica em `/swagger`. Endpoint de teste: `GET /ping`.

## Banco de dados (PostgreSQL)

A string de conexão `ConnectionStrings:Cofre` **nunca** vai para `appsettings*.json`. Os valores abaixo batem com o `docker-compose.yml` da raiz (`POSTGRES_DB` e `POSTGRES_USER` = `cofre`, porta 5432 em 127.0.0.1); a senha é a do seu `.env`.

Opção 1, user-secrets (desenvolvimento local), dentro de `backend/`:

```bash
dotnet user-secrets set "ConnectionStrings:Cofre" "Host=127.0.0.1;Port=5432;Database=cofre;Username=cofre;Password=<senha do .env>" --project src/CofreEletronico.Api
```

Opção 2, variável de ambiente (CI, contêiner, servidor):

```bash
export ConnectionStrings__Cofre="Host=127.0.0.1;Port=5432;Database=cofre;Username=cofre;Password=<senha do .env>"
```

Em **Development**, a Api aplica as migrations pendentes ao subir. Sem a string configurada, ela só registra um aviso e sobe sem banco (o `/ping` responde). Com a string configurada e o banco fora do ar, a inicialização falha com erro no log.

**Produção e servidores:**

- Nunca rode com `ASPNETCORE_ENVIRONMENT=Development`. Fora de Development a Api não aplica migrations; aplique-as por script revisado (`dotnet ef migrations script --idempotent`) ou por `dotnet ef migrations bundle`.
- Fora do localhost, exija TLS na string de conexão: `SSL Mode=Require` (mínimo) ou `SSL Mode=VerifyFull` (valida o certificado do servidor).
- Não use `Include Error Detail=true` fora da máquina local.

### Migrations

```bash
dotnet tool restore
dotnet ef migrations add <Nome> --project src/CofreEletronico.Infrastructure --startup-project src/CofreEletronico.Api --output-dir Persistencia/Migrations
dotnet ef migrations script --project src/CofreEletronico.Infrastructure --startup-project src/CofreEletronico.Api
dotnet ef migrations has-pending-model-changes --project src/CofreEletronico.Infrastructure --startup-project src/CofreEletronico.Api
```
