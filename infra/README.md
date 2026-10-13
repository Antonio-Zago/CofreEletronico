# Infra

Ambiente e implantação do Cofre Eletrônico:

- `docker-compose.yml` (raiz) com PostgreSQL, Mosquitto e pgAdmin para desenvolvimento local e para o servidor (T-0102)
- Configuração do Mosquitto: `mosquitto/config/mosquitto.conf`, `mosquitto/config/acl` e geração do `passwd` (T-0102; refinamento da ACL e TLS em T-0608)
- Dockerfile da API e compose de produção com TLS (T-1202)
- Deploy e backup do banco (T-1203, T-1204)

O CI fica em `../.github/workflows/` (T-0106).

Responsável: agente `infra-devops`.

## Estrutura

```
docker-compose.yml               # na raiz do repositório
.env.example                     # na raiz; copiar para .env (ignorado pelo git)
infra/
  mosquitto/
    criar-usuario.sh             # cria/remove usuários MQTT e ajusta permissões
    config/                      # montado em /mosquitto/config (somente leitura)
      mosquitto.conf
      acl
      passwd                     # GERADO pelo script, nunca versionado
  pgadmin/
    servers.json                 # servidor "postgres" pré-cadastrado no pgAdmin
```

## Variáveis (`.env`)

| Variável | Padrão | Uso |
|---|---|---|
| `POSTGRES_DB`, `POSTGRES_USER`, `POSTGRES_PASSWORD` | — | Banco criado no primeiro `up` |
| `PGADMIN_EMAIL`, `PGADMIN_PASSWORD` | — | Login do pgAdmin |
| `BIND_ADDRESS` | `127.0.0.1` | Interface do host para Postgres e pgAdmin |
| `POSTGRES_PORT` | `5432` | Porta do host para o Postgres |
| `PGADMIN_PORT` | `5050` | Porta do host para o pgAdmin |
| `MQTT_BIND_ADDRESS` | `0.0.0.0` | Interface do host para o MQTT (ESP32 precisam alcançar) |
| `MQTT_PORT` | `1883` | Porta do host para o MQTT (no servidor: `1884`) |

Dentro da rede do compose as portas são sempre as padrão (`postgres:5432`, `mosquitto:1883`); as variáveis acima só mudam o lado do host.

`infra/pgadmin/servers.json` não lê variáveis de ambiente: ele usa `Username: cofre` e `MaintenanceDB: cofre`, iguais ao `.env.example`. Se mudar `POSTGRES_USER`/`POSTGRES_DB`, ajuste o arquivo (ou edite a conexão no pgAdmin). O `servers.json` só é importado na primeira inicialização do volume `pgadmin-data`.

## Subir em desenvolvimento

Pré-requisitos: Docker com Compose v2 e um shell bash (Linux/macOS, ou Git Bash/WSL no Windows) para o script.

```bash
# 1. Variáveis
cp .env.example .env            # e troque as senhas

# 2. Usuários MQTT (OBRIGATÓRIO antes do primeiro up: o Mosquitto não
#    inicia se config/passwd não existir)
infra/mosquitto/criar-usuario.sh backend          # pede a senha
infra/mosquitto/criar-usuario.sh estante-dev-01   # ESP32 de dev (username = estanteId)

# 3. Subir
docker compose up -d
docker compose ps
docker compose logs -f mosquitto
```

- pgAdmin: http://localhost:5050 (login = `PGADMIN_EMAIL`/`PGADMIN_PASSWORD`; a senha do Postgres é pedida ao abrir o servidor).
- Postgres: `localhost:5432`.
- MQTT: `localhost:1883` (sem TLS em dev).

Parar: `docker compose down` (mantém volumes) ou `docker compose down -v` (apaga banco, dados do Mosquitto e pgAdmin).

## Subir no servidor que já tem Mosquitto nativo

O servidor Linux já roda um Mosquitto nativo (systemd) na 1883, que deve continuar rodando. O Mosquitto do container sobe ao lado dele em outra porta do host:

```bash
# Conferir o que já ocupa portas
sudo ss -tlnp | grep -E ':(1883|1884|5432|5050)\b'

cp .env.example .env
# Editar .env:
#   MQTT_PORT=1884
#   MQTT_BIND_ADDRESS=0.0.0.0
#   BIND_ADDRESS=127.0.0.1      # Postgres/pgAdmin não ficam expostos
#   (se houver Postgres nativo na 5432: POSTGRES_PORT=5433)
#   POSTGRES_PASSWORD e PGADMIN_PASSWORD: NUNCA deixe "troque-me";
#   use senhas geradas, ex.: openssl rand -base64 24

infra/mosquitto/criar-usuario.sh backend       # Enter = gera senha forte
infra/mosquitto/criar-usuario.sh <estanteId>

docker compose up -d

# Conferir: o nativo continua na 1883 e o docker-proxy aparece na 1884
sudo ss -tlnp | grep -E ':(1883|1884)\b'
```

Cuidados específicos deste servidor (dois brokers na mesma máquina):

- **Porta explícita.** O backend e o firmware (configuração em NVS) precisam apontar explicitamente para a porta do container (`1884`). Se a porta for omitida, o cliente usa a 1883 padrão e envia as credenciais para o **Mosquitto nativo**, não para o do projeto.
- **Usernames exclusivos.** Não reutilize no broker do container usernames (nem senhas) que existam no Mosquitto nativo; assim um erro de porta falha na autenticação em vez de conectar no broker errado.
- **Backend pela rede interna.** O backend fala com o broker pela rede do compose (`mosquitto:1883`) quando roda no mesmo compose, ou por `127.0.0.1:1884` quando roda no host. Nunca pelo IP público/internet. A porta publicada em `0.0.0.0` existe apenas para os ESP32.
- **Senhas.** Troque todos os `troque-me` do `.env` por senhas geradas (`openssl rand -base64 24`). Para MQTT, deixe o script gerar (Enter no prompt).
- **Firewall.** Libere a porta MQTT, por exemplo `sudo ufw allow 1884/tcp`. Mas atenção: portas publicadas pelo Docker são inseridas direto no iptables e **o ufw não as filtra**. Por isso Postgres e pgAdmin são publicados apenas em `127.0.0.1` (`BIND_ADDRESS`). Para restringir o MQTT por IP de origem, use a chain `DOCKER-USER` (avaliada antes das regras do Docker). Exemplo, aceitando a 1884 só da rede `203.0.113.0/24` (troque pela rede dos ESP32) na interface pública `eth0`:

  ```bash
  # A porta é a do CONTAINER (1883), pois o DNAT já ocorreu quando o pacote chega em DOCKER-USER.
  sudo iptables -I DOCKER-USER -i eth0 -p tcp --dport 1883 -m conntrack --ctorigdstport 1884        ! -s 203.0.113.0/24 -j DROP
  sudo iptables -L DOCKER-USER -n -v
  ```

  Regras de iptables não persistem após reboot; salve com `iptables-persistent` (`sudo netfilter-persistent save`) ou equivalente.
- **pgAdmin remoto.** Use túnel SSH: `ssh -L 5050:127.0.0.1:5050 usuario@servidor` e abra http://localhost:5050.
- **TLS.** O listener 8883 fica para T-0608/T-1202; até lá a porta MQTT trafega sem criptografia.

## Usuários MQTT

- `backend`: usuário da API .NET (publica `cofre/+/cmd`, assina `ack`, `estado`, `online`).
- Um usuário por ESP32, cujo **username é o `estanteId`** usado nos tópicos `cofre/{estanteId}/...`. A ACL usa `pattern ... cofre/%u/...`, então cada ESP32 só acessa os tópicos da própria estante. Usernames de dispositivo: apenas `[A-Za-z0-9_-]`; `backend` é reservado.

```bash
infra/mosquitto/criar-usuario.sh <usuario>            # pede a senha (sem eco); Enter = gera aleatória
MQTT_SENHA=... infra/mosquitto/criar-usuario.sh <usuario>   # não interativo (sem TTY e sem MQTT_SENHA: gera)
infra/mosquitto/criar-usuario.sh --remover <usuario>
infra/mosquitto/criar-usuario.sh --permissoes         # reaplica dono/permissões de passwd e acl

# Com o broker rodando, recarregar passwd/acl sem derrubar conexões:
docker compose kill -s HUP mosquitto
```

Regras de senha: mínimo de 16 caracteres; se nenhuma for informada, o script gera uma com `openssl rand -base64 24` (ou `/dev/urandom` + `base64`) e a exibe uma única vez. A senha nunca vai no argv: é passada ao container por variável de ambiente herdada (`-e MQTT_SENHA` sem valor) e entregue ao `mosquitto_passwd` por stdin.

O broker usa `use_username_as_clientid true`: o clientId é sempre o username, então há no máximo uma conexão ativa por usuário (uma nova conexão derruba a anterior). O backend deve manter uma única conexão MQTT, ou usar um usuário por instância.

O script usa `docker run eclipse-mosquitto:2` com o diretório `config/` montado com escrita (no compose ele é `:ro`), roda `mosquitto_passwd` e, como root dentro do container, faz `chown 1883:1883` (usuário `mosquitto`) e `chmod 0600` em `passwd` e `acl`. Isso evita os avisos do Mosquitto 2.x sobre permissões abertas (versões futuras recusam o arquivo). Consequências no Linux:

- `passwd` e `acl` ficam pertencendo ao uid 1883; para editar a `acl` no host use `sudo`, e depois de um `git pull` que altere a `acl` rode `--permissoes` novamente.
- No Windows (Docker Desktop) `chown/chmod` em bind mount não têm efeito; o Mosquitto apenas registra avisos de permissão no log, o que é aceitável em dev.

## Teste rápido do MQTT (dentro do container)

```bash
# Terminal 1: backend assina os acks da estante de dev
docker compose exec mosquitto mosquitto_sub -h localhost -p 1883 \
  -u backend -P '<senha-backend>' -t 'cofre/+/ack' -q 1 -v

# Terminal 2: ESP32 de dev publica um ack (deve aparecer no terminal 1)
docker compose exec mosquitto mosquitto_pub -h localhost -p 1883 \
  -u estante-dev-01 -P '<senha-esp32>' -t 'cofre/estante-dev-01/ack' -q 1 \
  -m '{"commandId":"teste","cofre":1,"resultado":"ok"}'

# Anônimo deve ser recusado ("Connection Refused: not authorised")
docker compose exec mosquitto mosquitto_pub -h localhost -p 1883 -t teste -m x

# ACL: o ESP32 publicando em outra estante não é entregue (o broker descarta em silêncio)
docker compose exec mosquitto mosquitto_pub -h localhost -p 1883 \
  -u estante-dev-01 -P '<senha-esp32>' -t 'cofre/outra-estante/ack' -q 1 -m x
```

Do host (ou de outra máquina), use a porta publicada: `mosquitto_sub -h <host> -p ${MQTT_PORT} ...`.

## Verificação da configuração

```bash
docker compose config --quiet                          # com .env
docker compose --env-file .env.example config --quiet  # sem .env
```
