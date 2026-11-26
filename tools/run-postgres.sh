#!/bin/bash
set -euo pipefail

ROOT_DIR=$(CDPATH= cd "$(dirname "$0")/.." && pwd)
cd "$ROOT_DIR"

if ! docker compose version >/dev/null 2>&1; then
  echo "Для запуска требуется Docker с плагином Compose." >&2
  exit 1
fi

if [[ -z ${POSTGRES_PASSWORD:-} ]]; then
  unset POSTGRES_PASSWORD
  if [[ ! -e .env && ! -L .env ]]; then
    password=$(openssl rand -hex 24)
    (umask 077; set -o noclobber; printf 'POSTGRES_PASSWORD=%s\n' "$password" > .env)
    unset password
  fi
fi

docker compose up --build --wait
echo "==> Приложение готово: http://127.0.0.1:8080"
