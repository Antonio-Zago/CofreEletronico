# Infra

Ambiente e implantação do Cofre Eletrônico:

- `docker-compose` com PostgreSQL, Mosquitto e pgAdmin para desenvolvimento local (T-0102)
- Configuração do Mosquitto: `mosquitto.conf`, geração do `passwd` e ACL por estante (T-0102, T-0608)
- Dockerfile da API e compose de produção com TLS (T-1202)
- Deploy e backup do banco (T-1203, T-1204)

O CI fica em `../.github/workflows/` (T-0106).

Responsável: agente `infra-devops`.
