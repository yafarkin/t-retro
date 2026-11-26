import { defineConfig } from '@playwright/test';

export default defineConfig({
  testDir: './e2e',
  fullyParallel: false,
  workers: 1,
  timeout: 180_000,
  use: {
    baseURL: 'http://127.0.0.1:4200',
    headless: true,
  },
  webServer: [
    {
      command: `bash -c '
        cd .. || exit 1
        export DOTNET_ROOT="\${DOTNET_ROOT:-/usr/local/share/dotnet}"
        export PATH="$DOTNET_ROOT:$PATH"
        export DOTNET_CLI_TELEMETRY_OPTOUT=1 NUGET_PACKAGES="$PWD/.tools/nuget/packages"
        if [ -n "\${TRETRO_E2E_POSTGRES:-}" ]; then
          export Database__Provider=PostgreSQL ConnectionStrings__Default="$TRETRO_E2E_POSTGRES"
        else
          export Database__Provider=SQLite ConnectionStrings__Default="Data Source=$PWD/.tools/e2e/t-retro.db"
        fi
        dotnet run --project src/Api --launch-profile http -- --migrate &&
        dotnet run --project src/Api --launch-profile http --no-build'`,
      url: 'http://127.0.0.1:5166/health/ready',
      reuseExistingServer: false,
      timeout: 120_000,
    },
    {
      command: 'npm start -- --host 127.0.0.1 --port 4200',
      url: 'http://127.0.0.1:4200',
      reuseExistingServer: false,
      timeout: 120_000,
    },
  ],
});
