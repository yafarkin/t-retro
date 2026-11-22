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
      command: "bash -c 'cd .. && export DOTNET_ROOT=/usr/local/share/dotnet PATH=/usr/local/share/dotnet:$PATH DOTNET_CLI_TELEMETRY_OPTOUT=1 NUGET_PACKAGES=$PWD/.tools/nuget/packages && dotnet run --project src/Api --launch-profile http -- --migrate && dotnet run --project src/Api --launch-profile http --no-build'",
      url: 'http://127.0.0.1:5166/health/live',
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
