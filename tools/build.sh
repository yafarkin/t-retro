#!/bin/bash
set -euo pipefail

ROOT_DIR=$(CDPATH= cd "$(dirname "$0")/.." && pwd)
cd "$ROOT_DIR"

OS_NAME=$(uname -s)
if [[ $OS_NAME != "Darwin" && $OS_NAME != "Linux" ]]; then
  echo "tools/build поддерживает macOS и Ubuntu Linux." >&2
  exit 1
fi

if [[ -x /usr/local/share/dotnet/dotnet ]]; then
  export DOTNET_ROOT=/usr/local/share/dotnet
  export PATH="$DOTNET_ROOT:$PATH"
elif [[ -n ${DOTNET_ROOT:-} && -x $DOTNET_ROOT/dotnet ]]; then
  export PATH="$DOTNET_ROOT:$PATH"
fi

require_command() {
  if ! command -v "$1" >/dev/null 2>&1; then
    echo "Не найдена команда '$1'. $2" >&2
    exit 1
  fi
}

require_command dotnet "Установите .NET SDK 10: https://dotnet.microsoft.com/download/dotnet/10.0"
require_command node "Установите Node.js из .nvmrc."
require_command npm "Установите npm вместе с Node.js."

EXPECTED_DOTNET=$(sed -n 's/.*"version":[[:space:]]*"\([^"]*\)".*/\1/p' global.json | head -n 1)
ACTUAL_DOTNET=$(dotnet --version)
if [[ $ACTUAL_DOTNET != "$EXPECTED_DOTNET" ]]; then
  echo "Требуется .NET SDK $EXPECTED_DOTNET, найден $ACTUAL_DOTNET." >&2
  exit 1
fi

EXPECTED_NODE_MAJOR=$(tr -d 'v[:space:]' < .nvmrc | cut -d. -f1)
ACTUAL_NODE_MAJOR=$(node --version | tr -d 'v[:space:]' | cut -d. -f1)
if [[ $ACTUAL_NODE_MAJOR != "$EXPECTED_NODE_MAJOR" ]]; then
  echo "Требуется Node.js $EXPECTED_NODE_MAJOR.x, найден $(node --version)." >&2
  exit 1
fi

EXPECTED_NPM_MAJOR=$(sed -n 's/.*"packageManager":[[:space:]]*"npm@\([^"]*\)".*/\1/p' web/package.json | cut -d. -f1)
ACTUAL_NPM_MAJOR=$(npm --version | cut -d. -f1)
if [[ $ACTUAL_NPM_MAJOR != "$EXPECTED_NPM_MAJOR" ]]; then
  echo "Требуется npm $EXPECTED_NPM_MAJOR.x, найден $(npm --version)." >&2
  exit 1
fi

export DOTNET_CLI_TELEMETRY_OPTOUT=1
export NUGET_PACKAGES="$ROOT_DIR/.tools/nuget/packages"
export PLAYWRIGHT_BROWSERS_PATH="$ROOT_DIR/.tools/playwright"

cleanup() {
  rm -rf "$ROOT_DIR/.tools/e2e" "$ROOT_DIR/web/test-results"
}
trap cleanup EXIT

echo "==> Восстановление и чистая сборка backend"
dotnet restore TRetro.slnx
dotnet clean TRetro.slnx --verbosity quiet
dotnet build TRetro.slnx --no-restore

echo "==> Тесты backend"
dotnet test TRetro.slnx --no-build --no-restore --verbosity minimal

echo "==> Чистая установка и сборка frontend"
rm -rf web/dist web/test-results
npm ci --prefix web
npm run build --prefix web

echo "==> Unit-тесты frontend"
npm test --prefix web -- --watch=false

echo "==> Установка Chromium для Playwright"
if [[ $OS_NAME == "Linux" ]]; then
  (cd web && npx playwright install --with-deps chromium)
else
  (cd web && npx playwright install chromium)
fi

echo "==> E2E-тесты"
rm -rf .tools/e2e
(cd web && npm run test:e2e)

echo "==> Сборка и все проверки завершены успешно"
