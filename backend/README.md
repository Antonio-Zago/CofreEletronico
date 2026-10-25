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
  CofreEletronico.Infrastructure.Tests  # modelo EF (sem banco), DI do Identity, JWT, refresh token e cifrador
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

Em Development o Swagger fica em `/swagger`. Endpoint de teste: `GET /ping` (anônimo).

A Api **não sobe** sem `Jwt:ChaveAssinatura` e sem `Criptografia:ChaveSegredos` (ver as seções abaixo).

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

## Autorização

Todo endpoint exige um access token válido, a menos que seja marcado com `AllowAnonymous()` (hoje: `/ping` e o grupo `/auth`). Isso vem da FallbackPolicy (usuário autenticado). Rotas administrativas usam a política `Politicas.Admin` (papel `Admin`). Respostas, sempre como ProblemDetails (`application/problem+json`):

- sem token ou com token inválido/expirado: **401**, com `WWW-Authenticate: Bearer`;
- autenticado, mas sem o papel exigido: **403**.

Os papéis vêm da claim `role` do access token, emitida no login/refresh. Uma mudança de papel só vale no próximo refresh (até 15 min).

## Administração de estantes

Rotas em `/admin/estantes` (papel `Admin`; contrato em [../docs/ARQUITETURA.md](../docs/ARQUITETURA.md), "Contrato REST: administração"): cadastro, listagem paginada, detalhe, edição, desativação/reativação (exclusão lógica) e regeneração de credenciais.

O cadastro (`POST /admin/estantes`) e a regeneração (`POST /admin/estantes/{id}/credenciais`) devolvem **uma única vez** as credenciais do ESP32: `deviceId`, `segredoHmac` (32 bytes em hex), `usuarioMqtt` (= `deviceId`) e `senhaMqtt` (24 caracteres `[A-Za-z0-9]`). O segredo HMAC fica cifrado no banco; a senha MQTT **não é armazenada** no backend. Essas respostas levam `Cache-Control: no-store`. Credenciais perdidas: regenere (o `deviceId` não muda).

Depois de cadastrar ou regenerar, na raiz do repositório:

1. Cadastre (ou troque) a senha no Mosquitto e **cole a `senhaMqtt` no prompt** do script (a digitação não ecoa):

   ```bash
   infra/mosquitto/criar-usuario.sh <deviceId>
   ```

   Não use `MQTT_SENHA=... infra/mosquitto/criar-usuario.sh` nem `export MQTT_SENHA=...` na linha de comando: a senha ficaria no histórico do shell (e visível no ambiente do processo).
2. Recarregue o broker para valer a senha nova: `docker compose kill -s HUP mosquitto`.
3. Grave no ESP32 o `deviceId`, a `senhaMqtt` e o `segredoHmac`.

**Revogação:** ao regenerar, o backend passa a assinar os comandos com o segredo novo na hora, mas a senha MQTT antiga **continua aceita pelo broker até o passo 2**, e o ESP32 só volta a operar depois do passo 3. A revogação só fica completa depois de trocar a senha no broker (passos 1 e 2) e regravar o ESP32 (passo 3).

Proteções do grupo `/admin` (todas por administrador, pela claim `sub` do token; token sem `sub` com Guid válido recebe 401):

- Rate limit geral de 120 requisições/min (`RateLimit:Admin`) e, somadas entre `POST /admin/estantes` e `POST /admin/estantes/{id}/credenciais`, 10/min (`RateLimit:AdminSensivel`). Cada subseção tem `Limite` e `JanelaSegundos`. Ao exceder: 429 com `Retry-After`.
- Concorrência otimista na tabela `estantes` (coluna de sistema `xmin`): se duas operações alteram a mesma estante ao mesmo tempo (por exemplo, duas regenerações), a segunda recebe 409 e nada é gravado. Na regeneração com 409, as credenciais não são devolvidas: recarregue e tente de novo.

## Administração de cofres

Rotas em `/admin/estantes/{estanteId}/cofres` (papel `Admin`; contrato em [../docs/ARQUITETURA.md](../docs/ARQUITETURA.md), "Contrato REST: administração", subseção "Cofres"): cadastro, listagem (ordenada por número, sem paginação), detalhe, edição, desativação/reativação (exclusão lógica) e manutenção (`/manutencao` e `/retirar-manutencao`).

- Corpo de cadastro e edição: `{ "numero": 1, "canalRele": 0, "tamanho": "medio" }`. `numero` >= 1, `canalRele` de 0 a 15 e `tamanho` exatamente `pequeno`, `medio` ou `grande`. Na resposta, `tamanho` e `status` (`livre`, `reservado`, `ocupado`, `manutencao`) também vêm em texto.
- Número e canal são únicos por estante (409 se já usados). É permitido cadastrar e editar cofres de estante desativada.
- Editar e desativar só com o cofre `livre` ou em `manutencao`; colocar em manutenção só a partir de `livre`; retirar só a partir de `manutencao`. Fora disso: 409 com o status atual na mensagem.
- Cofre de outra estante responde 404. Concorrência otimista na tabela `cofres` (`xmin`): alteração simultânea recebe 409 e nada é gravado.

## Erros inesperados

Fora de Development, uma exceção não tratada devolve 500 em ProblemDetails com título e detalhe genéricos (`Erro interno`), sem mensagem nem stack trace. A exceção completa vai só para o log, correlacionada pelo `traceId` da resposta. Em Development aparece a página de exceção do desenvolvedor.

## Administrador inicial

Na subida, em todos os ambientes (depois das migrations em Development), a Api cria um administrador (papéis `Admin` e `Usuario`) a partir da seção `AdminInicial`, **se ainda não existir nenhum Admin**. A seção é opcional e, como os outros segredos, **nunca** vai para `appsettings*.json` nem para o git.

| Chave | Obrigatória | Observação |
|---|---|---|
| `AdminInicial:Email` | sim, se `Senha` for informada | e-mail válido, até 256 caracteres |
| `AdminInicial:Senha` | sim, se `Email` for informado | até 128 caracteres, com a mesma política do cadastro: 8+ caracteres, com dígito, letra minúscula e letra maiúscula |
| `AdminInicial:Nome` | não | padrão `Administrador` |

Desenvolvimento local (user-secrets), dentro de `backend/`:

```bash
dotnet user-secrets set "AdminInicial:Email" "admin@seu-dominio.com" --project src/CofreEletronico.Api
dotnet user-secrets set "AdminInicial:Senha" "<senha forte>" --project src/CofreEletronico.Api
```

Servidor, contêiner ou CI (variável de ambiente):

```bash
export AdminInicial__Email="admin@seu-dominio.com"
export AdminInicial__Senha="<senha forte>"
```

**Produção:** prefira um secret do orquestrador (Docker secret, secret do Kubernetes) ou um arquivo montado só para leitura, com permissão restrita, a uma variável de ambiente definida em texto no compose ou no unit file. Variáveis de ambiente aparecem em `docker inspect`, em `/proc/<pid>/environ` e em dumps de diagnóstico.

Hoje a Api lê só user-secrets e variáveis de ambiente, sem ler arquivos em `/run/secrets`. Então o secret precisa ser repassado como `AdminInicial__Senha` pelo entrypoint do contêiner, só no processo da Api, e removido depois da primeira subida. A leitura direta de arquivos de secret ainda não existe.

Comportamento:

- Sem `AdminInicial`: só um log informativo.
- Sem a string de conexão: o seed é pulado com um aviso no log, e a Api sobe sem banco.
- Já existe algum Admin: nada é feito, e a senha de ninguém é alterada. Se `AdminInicial` continuar configurada, sai um **aviso** no log a cada subida, também em produção, lembrando de remover a senha.
- **Se o último Admin for apagado** (direto no banco, por exemplo) e `AdminInicial` ainda estiver configurada, a próxima subida **recria** um administrador com o e-mail e a senha configurados. É mais um motivo para não deixar a senha na configuração.
- O e-mail já pertence a uma conta comum: a conta **não** é promovida (seria escalação de privilégio por configuração). Aparece um aviso no log, e nenhum Admin é criado.
- Senha fora da política, ou `Email`/`Senha` incompletos ou inválidos: a Api **não sobe**, e a mensagem lista as regras violadas, sem mostrar a senha.

**Depois da primeira subida, apague a senha da configuração**:

- user-secrets: `dotnet user-secrets remove "AdminInicial:Senha" --project src/CofreEletronico.Api`
- servidor: remova a variável `AdminInicial__Senha`

A conta continua existindo, e o log de criação lembra disso. Em produção, use uma senha forte e única, gerada por um gerador de senhas e não reaproveitada de outro sistema. O log nunca mostra a senha, e o e-mail aparece mascarado (`a***@dominio`).

### Risco de bloqueio dirigido do administrador

O lockout (5 senhas erradas, 15 min de bloqueio) vale para qualquer conta, inclusive a de Admin. Quem souber o e-mail do administrador pode mantê-lo bloqueado errando a senha de propósito (negação de serviço dirigida). O rate limit por IP limita o ritmo, mas não impede um atacante com vários IPs. Quando uma conta com papel Admin é bloqueada, o log registra um aviso específico (`Conta administradora {UsuarioId} bloqueada por tentativas...`, sem e-mail), para monitoramento e alerta.

Mitigações:

- **E-mail do admin não público:** não use o e-mail de contato do sistema nem um endereço divulgado; prefira um alias exclusivo.
- **Pelo menos 2 administradores:** se um estiver bloqueado, o outro continua operando. A gestão de usuários pelo admin ainda não existe, e o `AdminInicial` só age quando não há nenhum Admin. Até lá, dar o papel `Admin` a uma segunda conta já cadastrada exige um operador inserir o vínculo em `usuarios_papeis`, direto no banco e de forma auditada.
- **MFA (futuro):** com segundo fator, o bloqueio por senha pode ficar menos agressivo para o admin sem enfraquecer a proteção.

## Chave de criptografia dos segredos das estantes

O segredo HMAC de cada estante (usado para assinar os comandos MQTT) é gravado no banco cifrado com AES-256-GCM (coluna `estantes.segredo_hmac`, formato `v1:` + base64 de nonce | texto cifrado | tag). A chave `Criptografia:ChaveSegredos` fica **fora do banco** e é segredo: **nunca** vai para `appsettings*.json` nem para o git. Deve ter **exatamente 32 bytes**, em base64 (44 caracteres). Sem ela, ou com base64 inválido ou tamanho errado, a Api falha na subida com uma mensagem dizendo o que configurar. Fora do ambiente `Testing`, chaves conhecidas ou triviais também são recusadas (a chave fictícia dos testes, bytes 0..31, e qualquer chave com todos os bytes iguais, como a toda zero). O `dotnet ef` (migrations) não precisa dela.

Gerar a chave (32 bytes aleatórios em base64):

```powershell
# PowerShell (Windows PowerShell 5.1 ou PowerShell 7)
$b = New-Object byte[] 32; [Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($b); [Convert]::ToBase64String($b)
```

```bash
openssl rand -base64 32
```

Desenvolvimento local (user-secrets), dentro de `backend/`:

```bash
dotnet user-secrets set "Criptografia:ChaveSegredos" "<chave gerada>" --project src/CofreEletronico.Api
```

Servidor, contêiner ou CI (variável de ambiente):

```bash
export Criptografia__ChaveSegredos="<chave gerada>"
```

> **Atenção: guarde a chave em local seguro (cofre de senhas/segredos) e faça backup.** Perder a chave torna os segredos gravados **irrecuperáveis**: seria preciso gerar um novo segredo para cada estante, regravá-lo no banco e reprovisioná-lo no ESP32 correspondente. Trocar a chave tem o mesmo efeito (a rotação com recifragem ainda não existe; o prefixo `v1:` está aí para permitir isso no futuro). Use uma chave diferente por ambiente.

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
