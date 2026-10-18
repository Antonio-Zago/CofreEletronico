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
| `cofre/{estanteId}/cmd` | Backend → ESP32 | `{ "commandId": "uuid", "cofre": 3, "acao": "abrir", "ts": 1760000000, "assinatura": "hmac-sha256" }` |
| `cofre/{estanteId}/ack` | ESP32 → Backend | `{ "commandId": "uuid", "cofre": 3, "resultado": "ok" \| "erro", "erro": "..." }` |
| `cofre/{estanteId}/estado` | ESP32 → Backend (retained) | `{ "cofres": [ { "n": 3, "porta": "fechada" } ] }` |
| `cofre/{estanteId}/online` | ESP32 → Backend (LWT + heartbeat, retained) | `"online"` / `"offline"` |

Regras de segurança dos comandos:
- `assinatura` = HMAC-SHA256 de `commandId|cofre|acao|ts` com o segredo da estante (gerado no cadastro).
- O ESP32 rejeita comando com assinatura inválida, `ts` fora da janela (ex.: 30 s) ou `commandId` já processado.
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

## Modelo de dados (evolução do DER.drawio)

| Tabela | Campos principais | Observação |
|---|---|---|
| `papeis` | id, nome | Papéis do ASP.NET Identity, fixos: `Usuario` e `Admin`. N:N com usuários via `usuarios_papeis` (no DER atual está 1:1) |
| `usuarios` | id, nome, email (único), senha_hash, cpf (opcional, único quando informado), telefone, criado_em | Usuário do ASP.NET Identity, existe só na Infrastructure; o domínio referencia apenas `usuario_id`. Inclui os campos técnicos do Identity (security_stamp, lockout etc.). CPF pedido só quando necessário (ex.: Pix), por LGPD |
| `usuarios_papeis` | usuario_id, papel_id | Tabela intermediária do Identity (PK composta). Cadastro recebe `Usuario`; um admin pode ter os dois papéis |
| `refresh_tokens` | id, usuario_id, token_hash (único), familia_id, criado_em, expira_em, sessao_expira_em, revogado_em, substituido_por_id | Refresh tokens rotativos. Guarda só o hash SHA-256. `familia_id` agrupa as rotações de um mesmo login, para revogação em cadeia em caso de reuso. `sessao_expira_em`: limite absoluto da sessão (login + 90 dias), copiado em cada rotação. `substituido_por_id` aponta para o token gerado na rotação |
| `estantes` | id, nome, endereco, latitude, longitude, device_id, segredo_hmac, online, ultimo_heartbeat | Renomeia `estante_cofres` |
| `cofres` | id, estante_id, numero, canal_rele, tamanho, status | status: livre / reservado / ocupado / manutencao |
| `tarifas` | id, tamanho, valor_por_hora, tempo_minimo | |
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
- Tamanhos de cofre e tabela de preços.
- Hospedagem de produção (VPS ou nuvem).
