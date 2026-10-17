import { defineConfig } from "@playwright/test";

export default defineConfig({
  testDir: "./tests/e2e",
  workers: 1,
  timeout: 60000,
  use: { baseURL: "http://localhost:3000", viewport: { width: 1366, height: 768 }, trace: "retain-on-failure" },
  webServer: [
    {
      command: "dotnet run --project ../../src/backend/Pos.Api/Pos.Api.csproj --no-launch-profile --urls http://localhost:5088",
      url: "http://localhost:5088/health/ready",
      reuseExistingServer: !process.env.CI,
      timeout: 120000,
      env: {
        ASPNETCORE_ENVIRONMENT: "Development",
        POSTGRES_CONNECTION_STRING: process.env.POSTGRES_CONNECTION_STRING ?? "Host=localhost;Port=5432;Database=universal_commerce;Username=commerce;Password=commerce_dev_password",
        AUTH_SIGNING_KEY: process.env.AUTH_SIGNING_KEY ?? "local-e2e-signing-key-at-least-32-bytes",
        DEMO_ADMIN_PASSWORD: "ChangeMe123!"
      }
    },
    { command: "npm run start", url: "http://localhost:3000", reuseExistingServer: !process.env.CI, timeout: 120000 }
  ]
});
