import { defineConfig } from '@playwright/test'

// Smoke test of an app created with `dotnet new inertia --auth`: STARTER_DIR is its folder, with the client already built.
const dir = process.env.STARTER_DIR
if (!dir) throw new Error('Set STARTER_DIR to the folder of an app created with dotnet new inertia --auth.')
const baseURL = 'http://localhost:5110'
// The same app in Production, where the demo account is switched off. Web servers start in order, so this one reuses the build.
export const productionURL = 'http://localhost:5111'

export default defineConfig({
  testDir: '.',
  workers: 1,
  forbidOnly: !!process.env.CI,
  reporter: process.env.CI ? [['github']] : 'list',
  use: { baseURL, trace: 'retain-on-failure' },
  projects: [{ name: 'chromium', use: { browserName: 'chromium' } }],
  webServer: [
    {
      command: `dotnet run --project "${dir}" -c Release --no-launch-profile --urls ${baseURL}`,
      env: { ASPNETCORE_ENVIRONMENT: 'Development' },
      url: baseURL,
      reuseExistingServer: false,
      timeout: 120_000,
    },
    {
      command: `dotnet run --project "${dir}" -c Release --no-build --no-launch-profile --urls ${productionURL}`,
      env: { ASPNETCORE_ENVIRONMENT: 'Production' },
      url: productionURL,
      reuseExistingServer: false,
      timeout: 120_000,
    },
  ],
})
