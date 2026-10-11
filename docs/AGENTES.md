# Estrutura de agentes

O desenvolvimento usa subagentes do Claude Code, um por especialidade, coordenados pela sessão principal, que atua como **orquestrador**. O trabalho é feito **tarefa a tarefa**: você escolhe a tarefa do [BACKLOG.md](BACKLOG.md) e o orquestrador cuida do resto.

```
Você ──/tarefa T-0602──► ORQUESTRADOR (sessão principal: CLAUDE.md + skill /tarefa)
                          │ lê BACKLOG.md + ARQUITETURA.md, checa dependências
                          ├──► backend-dotnet      (implementa)
                          ├──► mobile-flutter      (implementa)
                          ├──► firmware-esp32      (implementa)
                          ├──► infra-devops        (implementa)
                          ├──► docs-arquitetura    (DER, fluxograma, contratos)
                          ├──► qa-testes           (testes de integração/E2E, critérios de aceite)
                          └──► revisor-seguranca   (somente leitura, revisa antes de concluir)
```

O orquestrador não é um subagente porque subagentes não podem chamar outros subagentes. Ele é a sessão em que você conversa, guiada pelo [CLAUDE.md](../CLAUDE.md).

## Agentes

| Agente | Faz | Pode alterar |
|---|---|---|
| orquestrador | Roteia, monta o briefing, valida build/testes, aciona revisão, atualiza o status, propõe commit | Status no `docs/BACKLOG.md` |
| `backend-dotnet` | API .NET 8, EF Core, auth, MQTT, Pix, SignalR, jobs, simulador de ESP32 | `backend/` |
| `mobile-flutter` | App Flutter | `mobile/` |
| `firmware-esp32` | Firmware PlatformIO | `firmware/` |
| `infra-devops` | Monorepo, docker-compose, Mosquitto, CI, deploy | `infra/`, `.github/`, compose, arquivos de raiz |
| `docs-arquitetura` | Arquitetura, contratos, DER, fluxograma, README | `docs/`, `*.drawio`, `README.md` |
| `qa-testes` | Testes de integração/E2E, critérios de aceite | pastas de teste, `docs/qa/` |
| `revisor-seguranca` | Revisão de segurança | nada (somente leitura) |

Tarefas de **Hardware** são feitas por você; o orquestrador só gera o checklist.

Definições: [.claude/agents/](../.claude/agents/) · protocolo do comando: [.claude/skills/tarefa/SKILL.md](../.claude/skills/tarefa/SKILL.md).

## Como usar

```
/tarefa T-0101
```

O orquestrador então:

1. Localiza a tarefa e os critérios de aceite da US.
2. Verifica dependências e para se faltar algo.
3. Escolhe o especialista pelo componente.
4. Marca a tarefa como `Em andamento`.
5. Envia ao especialista um briefing completo.
6. Roda o build/teste do componente.
7. Aciona o `revisor-seguranca` em tarefas sensíveis (auth, HMAC/MQTT, ACL/TLS, pagamento, dados pessoais).
8. Marca como `Concluído`, resume e propõe o commit, que só é feito com seu ok.

## Regras

- Contratos (MQTT, modelo de dados, estados) só mudam via `docs-arquitetura`, com sua aprovação, e depois são propagados aos componentes afetados.
- O status oficial fica no `BACKLOG.md`; o `Backlog.xlsx` é sincronizado no fim de cada sprint.
- Nenhum segredo vai para o git.

## Ordem sugerida para a Sprint 1

T-0101 → T-0107 → T-0102 → T-0103 / T-0104 / T-0105 → T-0201..0203 → T-0301..0303 → T-0106 → T-2001
