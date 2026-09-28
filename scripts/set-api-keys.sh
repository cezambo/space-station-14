#!/usr/bin/env bash
# Stores API keys outside the repo (never under git) and loads them in new terminals.
set -euo pipefail

SECRETS_DIR="$HOME/.config/ss14jev"
SECRETS_FILE="$SECRETS_DIR/secrets.env"
BASHRC_LINE="[ -f \"$SECRETS_FILE\" ] && set -a && . \"$SECRETS_FILE\" && set +a"

mkdir -p "$SECRETS_DIR"
chmod 700 "$SECRETS_DIR"
touch "$SECRETS_FILE"
chmod 600 "$SECRETS_FILE"

ask() {
  local name="$1" value
  read -r -s -p "Cole a $name e aperte Enter (nada vai aparecer na tela; Enter vazio = manter a atual): " value
  echo
  if [ -n "$value" ]; then
    grep -v "^$name=" "$SECRETS_FILE" > "$SECRETS_FILE.tmp" || true
    printf '%s=%q\n' "$name" "$value" >> "$SECRETS_FILE.tmp"
    mv "$SECRETS_FILE.tmp" "$SECRETS_FILE"
    chmod 600 "$SECRETS_FILE"
    echo "  $name salva (${#value} caracteres)."
  fi
}

ask TYPESAFE_API_KEY
ask OPENROUTER_API_KEY

grep -qxF "$BASHRC_LINE" "$HOME/.bashrc" 2>/dev/null || echo "$BASHRC_LINE" >> "$HOME/.bashrc"

echo
echo "Pronto. Chaves em $SECRETS_FILE (fora do repositório, só seu usuário lê)."
echo "Feche este terminal e abra outro para as chaves valerem."
