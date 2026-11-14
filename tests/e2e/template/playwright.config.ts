import { defineConfig } from '@playwright/test'

// Smoke test of an app created with `dotnet new inertia --auth`: STARTER_DIR is its folder, with the client already built.
const dir = process.env.STARTER_DIR
if (!dir) throw new Error('Set STARTER_DIR to the folder of an app created with dotnet new inertia --auth.')
const baseURL = 'http://localhost:5110'

export default defineConfig({
  testDir: '.',
  workers: 1,
  forbidOnly: !!process.env.CI,
  reporter: process.env.CI ? [['github']] : 'list',
  use: { baseURL, trace: 'retain-on-failure' },
  projects: [{ name: 'chromium', use: { browserName: 'chromium' } }],
  webServer: {
    command: `dotnet run --project "${dir}" -c Release --no-launch-profile --urls ${baseURL}`,
    url: baseURL,
    reuseExistingServer: false,
    timeout: 120_000,
  },
})
