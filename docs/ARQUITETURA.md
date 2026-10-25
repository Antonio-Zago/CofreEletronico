# Arquitetura — Cofre Eletrônico

Sistema de locação de cofres eletrônicos: o usuário encontra uma estante pelo app, aluga um cofre por um período, paga via Pix e abre o cofre pelo celular. O backend envia o comando ao ESP32 da estante através do broker Mosquitto (MQTT).

Backlog completo em [BACKLOG.md](BACKLOG.md).

## Visão geral

```
App Flutter ──HTTPS/REST + SignalR──► API .NET 8 ──EF Core──► PostgreSQL
                                        │   ▲
                                        │   └── Webhook Pix (Mercado Pago)
                                        │
                                  MQTTnet (TLS)
                                        ▼
                                 Broker Mosquitto
                                        ▼
                     ESP32 (1 por estante) ── relés / fechaduras ── Cofres
```

| Componente | Tecnologia |
|---|---|
| Backend | .NET 8, ASP.NET Core, EF Core + Npgsql, ASP.NET Identity + JWT, MQTTnet, SignalR, Serilog |
| Banco | PostgreSQL |
| Broker | Mosquitto (TLS, usuário/senha por dispositivo, ACL) |
| Pagamento | Pix via Mercado Pago (QR code + webhook) |
| App | Flutter, Riverpod, go_router, dio, google_maps_flutter, geolocator, qr_flutter, Firebase Messaging |
| Firmware | ESP32, PlatformIO (Arduino), PubSubClient, ArduinoJson |
| Infra | Docker Compose, GitHub Actions |

Site de gerenciamento: fora do escopo atual. As funções de administrador ficam em endpoints `/admin` da API.

## Estrutura do repositório (proposta)

```
backend/
  src/CofreEletronico.Api             # endpoints, auth, Swagger, hub SignalR
  src/CofreEletronico.Application     # casos de uso, DTOs, validação
  src/CofreEletronico.Domain          # entidades e regras (estados de locação e cofre)
  src/CofreEletronico.Infrastructure  # EF Core, MQTT, Mercado Pago, jobs
  tests/
  tools/EspSimulator                  # simulador de ESP32 para dev sem hardware
mobile/                               # app Flutter
firmware/                             # projeto PlatformIO
infra/                                # docker-compose, mosquitto.conf, ACL
docs/
```

## Contrato MQTT

QoS 1. Conexão TLS. Cada ESP32 tem usuário/senha próprios e a ACL só permite acesso aos tópicos da sua estante.

| Tópico | Direção | Payload |
|---|---|---|
| `cofre/{estanteId}/cmd` | Backend → ESP32 (nunca retained) | `{ "commandId": "uuid", "cofre": 3, "canal": 5, "acao": "abrir", "ts": 1760000000, "assinatura": "hmac-sha256" }` |
| `cofre/{estanteId}/ack` | ESP32 → Backend | `{ "commandId": "uuid", "cofre": 3, "canal": 5, "resultado": "ok" \| "erro", "erro": "..." }` |
| `cofre/{estanteId}/estado` | ESP32 → Backend (retained) | `{ "cofres": [ { "n": 3, "porta": "fechada" } ] }` |
| `cofre/{estanteId}/online` | ESP32 → Backend (LWT + heartbeat, retained) | `"online"` / `"offline"` |

Identificação da estante:
- `{estanteId}` nos tópicos é o `device_id` da estante (ex.: `cofre/est-7k2m9q/cmd`), e não o id interno (Guid).
- O `device_id` também é o username MQTT do ESP32. A ACL do Mosquitto usa `%u` para restringir cada ESP32 aos próprios tópicos.
- O username `backend` é reservado para a API.

Campos do comando:
- `cofre`: número do cofre na estante, usado em log e no `ack`.
- `canal`: canal do relé a acionar, de 0 a 15.
- O backend é a fonte única do mapeamento número→canal (tabela `cofres`, coluna `canal_rele`). O ESP32 aciona o canal recebido e não mantém tabela própria.
- O `ack` ecoa `cofre` e `canal` para o backend conferir que o canal acionado é o esperado e para facilitar o diagnóstico de mapeamento errado.

Regras de segurança dos comandos:
- `assinatura` = HMAC-SHA256 de `deviceId|commandId|cofre|canal|acao|ts` com o segredo da estante (gerado no cadastro). `deviceId` é o `device_id` da estante de destino e não vai no payload: incluí-lo faz a assinatura valer só para aquela estante.
- O backend nunca publica `cmd` com a flag retain.
- O ESP32 rejeita (sem acionar relé):
  - assinatura inválida, incluindo a calculada com outro `deviceId` (o ESP32 assina com o seu próprio `device_id`, então comando destinado a outra estante falha na verificação);
  - `canal` fora de 0 a 15;
  - `ts` fora da janela (ex.: 30 s);
  - `commandId` já processado;
  - `cmd` recebido com a flag retain (descartado).
- O backend espera o `ack` por até 10 s. Sem resposta, devolve timeout ao app e registra o evento.

## Contrato REST: autenticação

Base `/auth`. JSON com nomes em camelCase. Todos os endpoints são anônimos e usam rate limit por IP (IPv6 agrupado por prefixo /64), com limites separados por grupo:
- `POST /auth/registro` e `POST /auth/login`: 10 requisições/min por IP, somadas entre os dois.
- `POST /auth/refresh` e `POST /auth/logout`: 60 requisições/min por IP, somadas entre os dois.
- Ao exceder: 429 com cabeçalho `Retry-After`. O app não trata 429 no refresh como logout: espera o `Retry-After` e tenta de novo.

Corpo de resposta com tokens (o mesmo para registro, login e refresh):

```json
{ "accessToken": "jwt", "accessTokenExpiraEm": "ISO-8601 UTC", "refreshToken": "opaco", "refreshTokenExpiraEm": "ISO-8601 UTC" }
```

| Método e rota | Corpo | Sucesso | Erros |
|---|---|---|---|
| `POST /auth/registro` | `{ nome, email, senha, telefone? }` | 201 + tokens (login automático; recebe o papel `Usuario`) | 400 validação (ProblemDetails); 409 e-mail já cadastrado (mensagem genérica, sem ecoar o valor) |
| `POST /auth/login` | `{ email, senha }` | 200 + tokens | 401 genérico para e-mail inexistente, senha errada ou conta bloqueada |
| `POST /auth/refresh` | `{ refreshToken }` | 200 + novos tokens (rotação: o refresh usado é revogado) | 401 se o token for inválido, expirado (inclusive por limite da sessão) ou revogado. Reuso de token já rotacionado revoga toda a família (todas as sessões derivadas daquele login) |
| `POST /auth/logout` | `{ refreshToken }` | 204, idempotente (token inexistente ou já revogado também devolve 204) | — |

Regras:
- Access token: JWT HS256, validade de 15 min. Claims: `sub` (usuario_id), `email`, `role` (um por papel), `jti`, `iss`, `aud`. A chave de assinatura fica em `Jwt:ChaveAssinatura` (mínimo de 32 bytes, via user-secrets ou variável de ambiente, nunca versionada).
- Refresh token: valor aleatório opaco de 32 bytes ou mais (base64url). O banco guarda apenas o hash SHA-256 (tabela `refresh_tokens`). Logout revoga o refresh token, e o access token continua válido até expirar.
- Duração da sessão: uma sessão (família de refresh tokens, iniciada num login ou registro) dura no máximo 90 dias desde o login, independentemente das rotações. Cada refresh token expira em `min(agora + 30 dias, sessao_expira_em)`. Depois do limite, o refresh devolve 401 e o usuário precisa logar de novo.
- Cadastro: CPF não é pedido (só quando necessário, ex.: Pix). Não há confirmação de e-mail no MVP (ver T-0306).
- Bloqueio por tentativas: 5 falhas bloqueiam a conta por 15 min. A resposta é o mesmo 401 genérico.
- Endpoints protegidos (demais rotas): `Authorization: Bearer <accessToken>`. Sem token ou com token inválido: 401. Sem o papel exigido: 403.

## Contrato REST: administração

Regras gerais do grupo:
- Base `/admin`. JSON com nomes em camelCase.
- Todas as rotas exigem `Authorization: Bearer <accessToken>` com o papel `Admin`. Sem token ou com token inválido: 401. Token sem claim `sub` válida (id do usuário): 401. Sem o papel: 403. Todos em ProblemDetails.
- Erro de validação: 400 (ProblemDetails). Recurso inexistente: 404. Conflito de regra de negócio: 409.
- Respostas nunca expõem o segredo HMAC nem a senha MQTT, exceto nas duas rotas que devolvem `credenciais` (abaixo), e apenas uma vez.
- Rate limit do grupo, particionado pelo `sub` do admin:
  - geral: 120 requisições/min;
  - rotas sensíveis (`POST /admin/estantes` e `POST /admin/estantes/{id}/credenciais`): 10 requisições/min, somadas entre as duas;
  - ao exceder: 429 com cabeçalho `Retry-After`.
- Erro inesperado: 500 em ProblemDetails com mensagem genérica (sem detalhes internos).

### Estantes

| Método e rota | Corpo | Sucesso | Erros |
|---|---|---|---|
| `POST /admin/estantes` | `{ nome, endereco, latitude, longitude }` | 201 + estante + `credenciais`; cabeçalho `Location: /admin/estantes/{id}` | 400 |
| `GET /admin/estantes?ativa=&pagina=&tamanhoPagina=` | — | 200 `{ itens: [estante], pagina, tamanhoPagina, total }`. Padrão: `pagina` 1, `tamanhoPagina` 20 (máximo 100). Sem `ativa`, lista todas. Ordenado por nome | 400 |
| `GET /admin/estantes/{id}` | — | 200 estante | 404 |
| `PUT /admin/estantes/{id}` | `{ nome, endereco, latitude, longitude }` | 200 estante | 400, 404; 409 se outra alteração da mesma estante for gravada ao mesmo tempo (concorrência) |
| `POST /admin/estantes/{id}/desativar` | — | 200 estante (idempotente) | 404; 409 se algum cofre estiver `reservado` ou `ocupado`; 409 se outra alteração da mesma estante for gravada ao mesmo tempo (concorrência) |
| `POST /admin/estantes/{id}/reativar` | — | 200 estante (idempotente) | 404; 409 se outra alteração da mesma estante for gravada ao mesmo tempo (concorrência) |
| `POST /admin/estantes/{id}/credenciais` | — | 200 `credenciais` novas. O backend passa a assinar com o novo segredo imediatamente. A revogação só fica completa depois que o admin (1) troca a senha do usuário no Mosquitto (`infra/mosquitto/criar-usuario.sh <deviceId>`, colando a senha no prompt, seguido de `docker compose kill -s HUP mosquitto`) e (2) regrava o ESP32 com o novo segredo e a nova senha. Até lá, a senha MQTT antiga continua aceita pelo broker e o ESP32 continua aceitando o segredo antigo | 404; 409 se outra alteração da mesma estante for gravada durante a regeneração (concorrência) |

Objeto estante:

```json
{ "id": "guid", "nome": "...", "endereco": "...", "latitude": 0.0, "longitude": 0.0, "deviceId": "est-7k2m9q", "online": false, "ultimoHeartbeat": "ISO-8601 UTC ou null", "ativa": true, "criadoEm": "ISO-8601 UTC" }
```

Objeto `credenciais` (exibido uma única vez):

```json
{ "deviceId": "est-7k2m9q", "segredoHmac": "64 caracteres hex", "usuarioMqtt": "est-7k2m9q", "senhaMqtt": "24 caracteres" }
```

- `segredoHmac`: 32 bytes em hexadecimal minúsculo (64 caracteres), para gravar na NVS do ESP32.
- `usuarioMqtt`: igual ao `deviceId`.
- `senhaMqtt`: 24 caracteres aleatórios. Não é armazenada no backend. O admin a cadastra no Mosquitto com `infra/mosquitto/criar-usuario.sh <deviceId>` e a grava no ESP32.
- Credenciais perdidas: a única saída é regenerá-las (`POST /admin/estantes/{id}/credenciais`).

Geração do `deviceId`: aleatório, formato `est-` + 6 caracteres `[a-z0-9]`, com nova tentativa em caso de colisão.

### Cofres

Base `/admin/estantes/{estanteId}/cofres`. Na tabela, `...` abrevia essa base.

| Método e rota | Corpo | Sucesso | Erros |
|---|---|---|---|
| `POST /admin/estantes/{estanteId}/cofres` | `{ numero, canalRele, tamanho }` | 201 cofre (status `livre`, ativo), com `Location: /admin/estantes/{estanteId}/cofres/{id}` | 400; 404 estante inexistente; 409 número ou canal já usado na estante |
| `GET /admin/estantes/{estanteId}/cofres` | — | 200 `[cofre]`, ordenado por número, sem paginação (no máximo 16 por estante) | 404 estante inexistente |
| `GET /admin/estantes/{estanteId}/cofres/{cofreId}` | — | 200 cofre | 404 (também se o cofre não pertencer à estante) |
| `PUT /admin/estantes/{estanteId}/cofres/{cofreId}` | `{ numero, canalRele, tamanho }` | 200 cofre | 400; 404; 409 se o cofre estiver `reservado` ou `ocupado`, se o número ou o canal já estiver em uso na estante, ou em concorrência |
| `POST .../{cofreId}/desativar` | — | 200 cofre (idempotente) | 404; 409 se `reservado` ou `ocupado`, ou em concorrência |
| `POST .../{cofreId}/reativar` | — | 200 cofre (idempotente) | 404; 409 concorrência |
| `POST .../{cofreId}/manutencao` | — | 200 cofre com status `manutencao` | 404; 409 se não estiver `livre` (transição inválida) ou em concorrência |
| `POST .../{cofreId}/retirar-manutencao` | — | 200 cofre com status `livre` | 404; 409 se não estiver em `manutencao`, ou em concorrência |

Objeto cofre:

```json
{ "id": "guid", "estanteId": "guid", "numero": 1, "canalRele": 0, "tamanho": "medio", "status": "livre", "ativo": true }
```

- `tamanho`: `pequeno` / `medio` / `grande`.
- `status`: `livre` / `reservado` / `ocupado` / `manutencao`.

Regras:
- `canalRele` de 0 a 15 e `numero` > 0, ambos únicos na estante.
- Edição (PUT) só com o cofre `livre` ou em `manutencao`. Trocar o canal com locação em andamento faria o comando acionar outro relé.
- É permitido cadastrar e editar cofres de estante desativada (para preparar ou reconfigurar), mas ela continua fora das buscas.
- Um cofre inativo não pode ser reservado.
- Controle de concorrência otimista em `cofres`: alterações simultâneas devolvem 409.

## Modelo de dados (evolução do DER.drawio)

| Tabela | Campos principais | Observação |
|---|---|---|
| `papeis` | id, nome | Papéis do ASP.NET Identity, fixos: `Usuario` e `Admin`. N:N com usuários via `usuarios_papeis` (no DER atual está 1:1) |
| `usuarios` | id, nome, email (único), senha_hash, cpf (opcional, único quando informado), telefone, criado_em | Usuário do ASP.NET Identity, existe só na Infrastructure; o domínio referencia apenas `usuario_id`. Inclui os campos técnicos do Identity (security_stamp, lockout etc.). CPF pedido só quando necessário (ex.: Pix), por LGPD |
| `usuarios_papeis` | usuario_id, papel_id | Tabela intermediária do Identity (PK composta). Cadastro recebe `Usuario`; um admin pode ter os dois papéis |
| `refresh_tokens` | id, usuario_id, token_hash (único), familia_id, criado_em, expira_em, sessao_expira_em, revogado_em, substituido_por_id | Refresh tokens rotativos. Guarda só o hash SHA-256. `familia_id` agrupa as rotações de um mesmo login, para revogação em cadeia em caso de reuso. `sessao_expira_em`: limite absoluto da sessão (login + 90 dias), copiado em cada rotação. `substituido_por_id` aponta para o token gerado na rotação |
| `estantes` | id, nome, endereco, latitude, longitude, device_id (único), segredo_hmac (cifrado), online, ultimo_heartbeat, ativa, criado_em | Renomeia `estante_cofres`. `device_id`: curto, gerado no cadastro, formato `est-` + 6 caracteres `[a-z0-9]` (ex.: `est-7k2m9q`); é o identificador da estante nos tópicos MQTT e o username do ESP32 no Mosquitto. `segredo_hmac`: guardado cifrado (AES-GCM, chave da aplicação fora do banco), porque o backend precisa do valor original para assinar os comandos. latitude e longitude: double precision com CHECK de faixa (-90..90 e -180..180). `ativa = false` é a exclusão lógica (a estante sai das buscas, mas o histórico é preservado) |
| `cofres` | id, estante_id, numero, canal_rele, tamanho, status, ativo | tamanho: pequeno / medio / grande. status: livre / reservado / ocupado / manutencao. `numero` é único dentro da estante. `canal_rele`: de 0 a 15 (placa de 16 relés por estante), único dentro da estante. `ativo = false` é a exclusão lógica |
| `tarifas` | id, tamanho (único), valor_por_hora, tempo_minimo | Uma tarifa por tamanho, válida para todas as estantes. valor_por_hora: numeric(10,2), em BRL. tempo_minimo: em minutos. A locação guarda o valor cobrado, então mudar a tarifa não afeta locações existentes |
| `locacoes` | id, usuario_id, cofre_id, inicio, fim_previsto, fim_real, status, valor | Substitui `usuarios_cofres`. status: aguardando_pagamento / ativa / encerrada / cancelada / expirada |
| `pagamentos` | id, locacao_id, mp_payment_id, qr_code, status, valor, pago_em | Uma locação pode ter vários (extensões) |
| `eventos_cofre` | id, cofre_id, usuario_id, command_id, acao, resultado, criado_em, respondido_em | Auditoria de todos os comandos |

Convenções: tabelas e colunas em snake_case português; chaves primárias Guid. As tabelas auxiliares do Identity seguem a mesma convenção: `usuarios_claims`, `usuarios_logins`, `usuarios_tokens` e `papeis_claims`.

## Fluxo de locação

1. Usuário faz login e vê as estantes próximas no mapa.
2. Escolhe um cofre livre e a duração. A API cria a `locacao` (aguardando_pagamento), reserva o cofre por ~10 min e gera um Pix no Mercado Pago.
3. O app exibe o QR code / copia-e-cola.
4. O webhook do Mercado Pago confirma o pagamento → locação `ativa` → app notificado via SignalR.
5. Usuário toca **Abrir** → API valida (dono, período vigente, estante online) → publica `cmd` → ESP32 aciona o relé → publica `ack` → API registra o evento e responde ao app.
6. Usuário encerra (abertura final e cofre liberado) ou estende (novo Pix). Ao vencer, a locação é marcada como `expirada` e o usuário é notificado.

Jobs em background: cancelar reservas com Pix não pago, expirar locações vencidas, marcar estantes offline sem heartbeat.

## Pontos em aberto

- Política para itens não retirados após o fim da locação (cobrança de tempo extra? bloqueio?).
- Valores das tarifas (definidos pelo admin via T-0404).
- Hospedagem de produção (VPS ou nuvem).
