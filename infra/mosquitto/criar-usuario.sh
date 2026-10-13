#!/usr/bin/env bash
# Cria/atualiza usuários do Mosquitto em infra/mosquitto/config/passwd
# (arquivo NUNCA versionado) e ajusta as permissões de passwd e acl.
#
# Uso:
#   infra/mosquitto/criar-usuario.sh <usuario>
#   infra/mosquitto/criar-usuario.sh --remover <usuario>
#   infra/mosquitto/criar-usuario.sh --permissoes
#
# - Senha: variável de ambiente MQTT_SENHA, ou digitada no terminal (sem eco).
#   Se ficar vazia, uma senha aleatória forte é gerada e exibida UMA vez.
#   Mínimo de 16 caracteres. A senha nunca é passada como argumento de linha
#   de comando (não aparece em `ps` nem no histórico do shell).
# - Usuário "backend" = API .NET. Usuário de ESP32 = estanteId (ver acl).
# - Deve ser rodado ANTES do primeiro `docker compose up`: o Mosquitto não
#   inicia se /mosquitto/config/passwd não existir.
# - Com o broker rodando, recarregue com: docker compose kill -s HUP mosquitto
#
# Por que roda via `docker run` e não `docker compose run`: no compose o
# diretório de config é montado somente leitura (:ro). Aqui ele é montado com
# escrita, só durante a execução do comando. Dentro do container (root) o
# script faz chown para o usuário mosquitto (uid 1883) e chmod 0600, evitando
# os avisos/recusa do Mosquitto 2.x por permissões abertas em passwd/acl.
set -euo pipefail

IMAGEM="${MOSQUITTO_IMAGE:-eclipse-mosquitto:2}"
TAMANHO_MINIMO=16
DIR_SCRIPT="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
DIR_CONFIG="$DIR_SCRIPT/config"

uso() {
  sed -n '2,13p' "$0" | sed 's/^# \{0,1\}//'
  exit 1
}

# Git Bash no Windows: evita conversão automática de caminhos (/mosquitto/...)
# e usa o caminho Windows do diretório para o bind mount.
if pwd -W >/dev/null 2>&1; then
  export MSYS_NO_PATHCONV=1
  DIR_CONFIG_MOUNT="$(cd "$DIR_CONFIG" && pwd -W)"
else
  DIR_CONFIG_MOUNT="$DIR_CONFIG"
fi

# Executa comandos num container descartável com o config montado (rw).
# MQTT_USUARIO/MQTT_SENHA são herdados do ambiente exportado (-e sem valor),
# para que a senha não apareça no argv do docker.
no_container() {
  export MQTT_USUARIO="${MQTT_USUARIO:-}" MQTT_SENHA="${MQTT_SENHA:-}"
  docker run --rm -i \
    -e MQTT_USUARIO \
    -e MQTT_SENHA \
    -v "$DIR_CONFIG_MOUNT:/mosquitto/config" \
    --entrypoint sh "$IMAGEM" -c "$1"
}

AJUSTAR_PERMISSOES='
  cd /mosquitto/config
  [ -f passwd ] || touch passwd
  chown mosquitto:mosquitto passwd acl 2>/dev/null || true
  chmod 0600 passwd acl 2>/dev/null || true
'

# Grava a senha via stdin do mosquitto_passwd (sem -b, que exige a senha no argv).
# Usa -c (criar arquivo) apenas quando passwd ainda não existe ou está vazio.
GRAVAR_SENHA='
  set -e
  cd /mosquitto/config
  if [ -s passwd ]; then OPC=""; else OPC="-c"; fi
  printf "%s\n%s\n" "$MQTT_SENHA" "$MQTT_SENHA" | mosquitto_passwd $OPC passwd "$MQTT_USUARIO" >/dev/null
'

gerar_senha() {
  if command -v openssl >/dev/null 2>&1; then
    openssl rand -base64 24
  else
    head -c 24 /dev/urandom | base64
  fi | tr -d '\n'
}

validar_usuario() {
  if [[ ! "$1" =~ ^[A-Za-z0-9_-]+$ ]]; then
    echo "Erro: usuário inválido '$1'. Use apenas [A-Za-z0-9_-] (vira parte do tópico cofre/<usuario>/...)." >&2
    exit 1
  fi
}

[[ $# -ge 1 ]] || uso

case "$1" in
  -h|--help)
    uso
    ;;
  --permissoes)
    no_container "$AJUSTAR_PERMISSOES"
    echo "Permissões de passwd/acl ajustadas."
    ;;
  --remover)
    [[ $# -eq 2 ]] || uso
    validar_usuario "$2"
    MQTT_USUARIO="$2" no_container "
      set -e
      $AJUSTAR_PERMISSOES
      mosquitto_passwd -D passwd \"\$MQTT_USUARIO\"
    "
    echo "Usuário '$2' removido. Recarregue: docker compose kill -s HUP mosquitto"
    ;;
  -*)
    uso
    ;;
  *)
    [[ $# -eq 1 ]] || uso
    USUARIO="$1"
    validar_usuario "$USUARIO"

    SENHA="${MQTT_SENHA:-}"
    GERADA=0
    if [[ -z "$SENHA" && -t 0 ]]; then
      read -r -s -p "Senha para '$USUARIO' (Enter = gerar aleatória): " SENHA; echo
      if [[ -n "$SENHA" ]]; then
        read -r -s -p "Confirme a senha: " SENHA2; echo
        [[ "$SENHA" == "$SENHA2" ]] || { echo "Erro: senhas não conferem." >&2; exit 1; }
      fi
    fi
    if [[ -z "$SENHA" ]]; then
      SENHA="$(gerar_senha)"
      GERADA=1
    fi
    if (( ${#SENHA} < TAMANHO_MINIMO )); then
      echo "Erro: a senha deve ter pelo menos $TAMANHO_MINIMO caracteres." >&2
      exit 1
    fi

    MQTT_USUARIO="$USUARIO" MQTT_SENHA="$SENHA" no_container "
      set -e
      $GRAVAR_SENHA
      $AJUSTAR_PERMISSOES
    "
    echo "Usuário '$USUARIO' criado/atualizado em infra/mosquitto/config/passwd."
    if (( GERADA )); then
      echo "Senha gerada (exibida somente agora; guarde-a no gerenciador de segredos):"
      echo "  $SENHA"
    fi
    echo "Se o broker já estiver rodando: docker compose kill -s HUP mosquitto"
    ;;
esac
