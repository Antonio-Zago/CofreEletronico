---
name: tarefa
description: Orquestra a execução de UMA tarefa do backlog (T-xxxx) delegando ao subagente especialista, validando, revisando e atualizando o status no docs/BACKLOG.md. Use quando o usuário digitar /tarefa T-xxxx ou pedir para executar uma tarefa do backlog.
---

# /tarefa T-xxxx — protocolo do orquestrador

Argumento: o ID da tarefa (ex.: `T-0602`). Sem ID válido, peça ao usuário e pare.

## 1. Localizar
- Encontre a linha da tarefa em `docs/BACKLOG.md`: texto, Componente, Prioridade, Sprint, Status.
- Identifique a US a que ela pertence e copie os **critérios de aceite** da US.
- Se o Status já for `Concluído`, avise e pergunte se é para refazer.

## 2. Checar dependências
- Deduza as dependências pela lógica do backlog e da arquitetura (ex.: T-0302 precisa de T-0301; T-0801 precisa de T-0603 e T-0704; tarefas de app precisam do endpoint de backend correspondente; qualquer tarefa de um componente precisa do esqueleto T-0103/T-0104/T-0105).
- Confira no BACKLOG.md e no repositório se elas estão feitas. Se faltar algo, liste o que falta e **pare** (sugira a ordem).

## 3. Rotear
Use a tabela de roteamento do `CLAUDE.md`. Casos especiais:
- **Hardware:** não delegue. Gere um checklist/lista de compras em `docs/hardware/T-xxxx.md` e deixe a conclusão para o usuário.
- **Multi-componente** (ex.: T-0607 testes + simulador, T-0101 pastas de todos): quebre em subtarefas e delegue em sequência.
- **Contrato afetado:** se a tarefa exige mudar `docs/ARQUITETURA.md`, primeiro mostre a mudança proposta ao usuário, depois acione `docs-arquitetura`, e só então o especialista.

## 4. Marcar início
Altere o Status da tarefa para `Em andamento` em `docs/BACKLOG.md`.

## 5. Briefing ao especialista
Chame o subagente (ferramenta Agent, `subagent_type` = nome do agente) com um prompt **autocontido** contendo:
- ID e texto da tarefa, US e critérios de aceite relevantes;
- trechos da `docs/ARQUITETURA.md` que se aplicam (copie-os, não apenas referencie);
- arquivos/pastas existentes relevantes e decisões já tomadas em tarefas anteriores;
- o que NÃO faz parte desta tarefa (para evitar escopo extra);
- o comando de verificação esperado;
- o formato de retorno: arquivos alterados, como verificou, pendências/propostas de contrato.

## 6. Validar
Rode você mesmo o comando de verificação do componente (ver `CLAUDE.md`). Se falhar, devolva o erro ao mesmo especialista (SendMessage para continuar o agente) — no máximo 2 ciclos; depois, pare e explique ao usuário.

## 7. Revisar
Acione `revisor-seguranca` se a tarefa tocar em: autenticação/JWT/papéis, HMAC/comandos MQTT, ACL/TLS/credenciais, webhook/pagamento, dados pessoais (CPF, e-mail, telefone), rate limit/auditoria. Passe a lista de arquivos alterados.
- Achado **bloqueante** → volta ao especialista (máx. 2 ciclos), depois escala ao usuário.
- **Recomendações** → registre no resumo final.
Para tarefas que fecham uma US inteira, acione também `qa-testes` para conferir os critérios de aceite.

## 8. Concluir
- Status → `Concluído` (ou `Bloqueado` com motivo) em `docs/BACKLOG.md`.
- Resumo curto ao usuário: o que mudou (links para arquivos), como foi verificado, recomendações do revisor, próxima tarefa sugerida.
- Proponha a mensagem de commit `T-xxxx: <descrição>` e **aguarde aprovação** antes de commitar. Não faça push.
