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

## Modelo de dados (evolução do DER.drawio)

| Tabela | Campos principais | Observação |
|---|---|---|
| `papeis` | id, nome | Papéis do ASP.NET Identity, fixos: `Usuario` e `Admin`. N:N com usuários via `usuarios_papeis` (no DER atual está 1:1) |
| `usuarios` | id, nome, email (único), senha_hash, cpf (opcional, único quando informado), telefone, criado_em | Usuário do ASP.NET Identity, existe só na Infrastructure; o domínio referencia apenas `usuario_id`. Inclui os campos técnicos do Identity (security_stamp, lockout etc.). CPF pedido só quando necessário (ex.: Pix), por LGPD |
| `usuarios_papeis` | usuario_id, papel_id | Tabela intermediária do Identity (PK composta). Cadastro recebe `Usuario`; um admin pode ter os dois papéis |
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
