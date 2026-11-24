#!/bin/bash
set -euo pipefail

ROOT_DIR=$(CDPATH= cd "$(dirname "$0")/.." && pwd)
cd "$ROOT_DIR"

OS_NAME=$(uname -s)
if [[ $OS_NAME != "Darwin" && $OS_NAME != "Linux" ]]; then
  echo "tools/run поддерживает macOS и Ubuntu Linux." >&2
  exit 1
fi

if [[ -x /usr/local/share/dotnet/dotnet ]]; then
  export DOTNET_ROOT=/usr/local/share/dotnet
  export PATH="$DOTNET_ROOT:$PATH"
elif [[ -n ${DOTNET_ROOT:-} && -x $DOTNET_ROOT/dotnet ]]; then
  export PATH="$DOTNET_ROOT:$PATH"
fi

for command in dotnet node npm curl; do
  if ! command -v "$command" >/dev/null 2>&1; then
    echo "Не найдена команда '$command'. Сначала выполните ./tools/build.sh." >&2
    exit 1
  fi
done

if [[ ! -f src/Api/bin/Debug/net10.0/Api.dll || ! -x web/node_modules/.bin/ng ]]; then
  echo "Приложение ещё не собрано. Выполните ./tools/build.sh." >&2
  exit 1
fi

export DOTNET_CLI_TELEMETRY_OPTOUT=1
export NG_CLI_ANALYTICS=false
export NUGET_PACKAGES="$ROOT_DIR/.tools/nuget/packages"

backend_pid=
frontend_pid=
cleanup() {
  trap - EXIT INT TERM
  if [[ -n $backend_pid ]] && kill -0 "$backend_pid" 2>/dev/null; then
    kill "$backend_pid" 2>/dev/null || true
  fi
  if [[ -n $frontend_pid ]] && kill -0 "$frontend_pid" 2>/dev/null; then
    kill "$frontend_pid" 2>/dev/null || true
  fi
  if [[ -n $backend_pid ]]; then
    wait "$backend_pid" 2>/dev/null || true
  fi
  if [[ -n $frontend_pid ]]; then
    wait "$frontend_pid" 2>/dev/null || true
  fi
}
trap cleanup EXIT
trap 'exit 130' INT
trap 'exit 143' TERM

echo "==> Миграции SQLite"
dotnet run --project src/Api --launch-profile http -- --migrate

echo "==> Запуск API на http://127.0.0.1:5166"
dotnet run --project src/Api --launch-profile http --no-build &
backend_pid=$!

echo "==> Запуск Angular на http://127.0.0.1:4200"
(
  cd web
  exec ./node_modules/.bin/ng serve --proxy-config proxy.conf.json --host 127.0.0.1 --port 4200
) &
frontend_pid=$!

wait_url() {
  local url=$1
  local pid=$2
  local name=$3
  for ((attempt = 1; attempt <= 60; attempt++)); do
    if ! kill -0 "$pid" 2>/dev/null; then
      echo "$name завершился во время запуска." >&2
      return 1
    fi
    if curl -fsS "$url" >/dev/null 2>&1; then
      return 0
    fi
    sleep 1
  done
  echo "$name не стал доступен за 60 секунд: $url" >&2
  return 1
}

wait_url http://127.0.0.1:5166/health/ready "$backend_pid" API
wait_url http://127.0.0.1:4200 "$frontend_pid" Angular

echo "==> Приложение готово: http://127.0.0.1:4200"
echo "==> Нажмите Ctrl+C для остановки API и Angular"

status=0
while kill -0 "$backend_pid" 2>/dev/null && kill -0 "$frontend_pid" 2>/dev/null; do
  sleep 1
done
if ! kill -0 "$backend_pid" 2>/dev/null; then
  wait "$backend_pid" || status=$?
fi
if ! kill -0 "$frontend_pid" 2>/dev/null; then
  wait "$frontend_pid" || status=$?
fi
exit "$status"
