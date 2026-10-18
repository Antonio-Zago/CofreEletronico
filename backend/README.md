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
  CofreEletronico.Infrastructure.Tests  # modelo EF (sem banco), DI do Identity, JWT e refresh token
  CofreEletronico.Api.Tests          # endpoints via WebApplicationFactory (portas de banco trocadas por fakes)
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

A Api **não sobe** sem `Jwt:ChaveAssinatura` (ver a seção abaixo).

## Autenticação (JWT)

Endpoints em `/auth` (`registro`, `login`, `refresh`, `logout`), contrato em [../docs/ARQUITETURA.md](../docs/ARQUITETURA.md). Rotas protegidas usam `Authorization: Bearer <accessToken>`; no Swagger, use o botão **Authorize**.

`Jwt:Emissor` e `Jwt:Audiencia` estão no `appsettings.json` (não são segredos). A chave de assinatura HS256 `Jwt:ChaveAssinatura` é segredo: **nunca** vai para `appsettings*.json` nem para o git. Mínimo de 32 bytes; sem ela (ou com menos), a Api falha na subida com uma mensagem dizendo o que configurar.

**A chave deve ser gerada aleatoriamente** por um gerador criptográfico (comandos abaixo). Não use frase, senha, palavra do dicionário nem texto digitado à mão: a Api só confere o tamanho (32 bytes ou mais), não a entropia, e uma chave adivinhável permite a qualquer um forjar tokens de qualquer usuário, inclusive Admin.

Gerar uma chave aleatória (48 bytes em base64, 64 caracteres):

```powershell
# PowerShell (Windows PowerShell 5.1 ou PowerShell 7)
$b = New-Object byte[] 48; [Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($b); [Convert]::ToBase64String($b)
```

```bash
openssl rand -base64 48
```

Desenvolvimento local (user-secrets), dentro de `backend/`:

```bash
dotnet user-secrets set "Jwt:ChaveAssinatura" "<chave gerada>" --project src/CofreEletronico.Api
```

Servidor, contêiner ou CI (variável de ambiente):

```bash
export Jwt__ChaveAssinatura="<chave gerada>"
```

Use uma chave diferente por ambiente. Trocar a chave invalida todos os access tokens emitidos (os usuários renovam pelo refresh token).

Rate limit por IP (IPv6 agrupado por prefixo /64), em duas políticas com contadores independentes: `/auth/registro` + `/auth/login` com 10 requisições/min somadas (`RateLimit:AuthCredenciais`) e `/auth/refresh` + `/auth/logout` com 60/min somadas (`RateLimit:AuthSessao`). Cada subseção tem `Limite` e `JanelaSegundos`. Ao exceder: 429 com `Retry-After`. Atrás de proxy reverso o limite só funciona por cliente depois de configurar ForwardedHeaders (T-1203).

Validades (padrões do contrato, com teto validado na subida): access token de 15 min (`Jwt:ValidadeAccessToken`, 1 a 15 min), refresh token de 30 dias (`Jwt:ValidadeRefreshToken`, 1 a 30 dias) e sessão de 90 dias desde o login (`Jwt:ValidadeSessao`, 1 a 90 dias). Cada refresh token expira em `min(agora + 30 dias, sessao_expira_em)`.

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
