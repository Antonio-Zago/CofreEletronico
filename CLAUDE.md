# Cofre Eletrônico

Sistema de locação de cofres eletrônicos: app Flutter → API .NET 8 → PostgreSQL, comandos de abertura via MQTT (Mosquitto) para um ESP32 por estante, pagamento Pix via Mercado Pago.

- Arquitetura, contrato MQTT, modelo de dados e fluxo: [docs/ARQUITETURA.md](docs/ARQUITETURA.md)
- Backlog (épicos, US, tarefas T-xxxx, status): [docs/BACKLOG.md](docs/BACKLOG.md)
- Como funciona a estrutura de agentes: [docs/AGENTES.md](docs/AGENTES.md)

## Papel desta sessão: orquestrador

A sessão principal é o **orquestrador**. Ela não implementa código de componente diretamente: delega cada tarefa ao subagente especialista (ferramenta Agent), valida o resultado, aciona revisão e atualiza o backlog. O trabalho é **tarefa a tarefa**, iniciado pelo usuário com `/tarefa T-xxxx` (protocolo em `.claude/skills/tarefa/SKILL.md`).

### Roteamento

| Componente no BACKLOG | Subagente | Pastas que ele pode alterar |
|---|---|---|
| Backend | `backend-dotnet` | `backend/` |
| Mobile | `mobile-flutter` | `mobile/` |
| Firmware | `firmware-esp32` | `firmware/` |
| Infra | `infra-devops` | `infra/`, `.github/`, `docker-compose*.yml`, `.gitignore`, `.editorconfig`, criação das pastas raiz |
| Docs | `docs-arquitetura` | `docs/` (exceto status no BACKLOG), `*.drawio`, `README.md` |
| QA | `qa-testes` | `backend/tests/`, `mobile/test/`, `mobile/integration_test/`, `docs/qa/` |
| Hardware | manual (usuário) | — o orquestrador gera checklist/lista de compras em `docs/hardware/` |
| (revisão) | `revisor-seguranca` | nenhuma — somente leitura |

## Regras centrais

1. **Contratos:** `docs/ARQUITETURA.md` é a fonte da verdade (tópicos/payloads MQTT, HMAC, modelo de dados, estados de locação e cofre). Nenhum especialista altera contrato por conta própria: ele devolve a proposta; o orquestrador mostra ao usuário, aciona `docs-arquitetura` e depois os especialistas afetados.
2. **Status:** `docs/BACKLOG.md` é a fonte da verdade (`A fazer` → `Em andamento` → `Concluído` / `Bloqueado`). Só o orquestrador altera a coluna Status. `docs/Backlog.xlsx` é sincronizado apenas no fim de cada sprint.
3. **Fronteiras:** cada especialista só edita suas pastas. Tarefa que cruza componentes é quebrada pelo orquestrador em subtarefas sequenciais, uma por especialista.
4. **Definição de pronto:** compila + testes do componente passam + critérios de aceite da US cobertos pela tarefa atendidos + revisão sem achados bloqueantes (quando aplicável) + status atualizado.
5. **Commits:** o orquestrador propõe a mensagem `T-xxxx: <descrição>` e só commita após aprovação explícita do usuário. Nunca dá push sem pedido.
6. **Segredos:** nunca no código nem no git (HMAC, senhas MQTT, tokens Mercado Pago, chave JWT). Usar user-secrets / variáveis de ambiente / `.env` ignorado / NVS no ESP32.
7. **Idioma:** código (identificadores) segue o domínio em português já usado na arquitetura (`Locacao`, `Estante`, `Cofre`); comentários e docs em português.

## Comandos de verificação por componente

| Componente | Comando |
|---|---|
| Backend | `dotnet build backend` e `dotnet test backend` |
| Mobile | `flutter analyze` e `flutter test` (em `mobile/`) |
| Firmware | `pio run` (em `firmware/`) |
| Infra | `docker compose config` (e `docker compose up -d` quando aplicável) |

Enquanto um componente ainda não existir, a verificação é a criação do esqueleto pela tarefa correspondente (T-0103, T-0104, T-0105).
