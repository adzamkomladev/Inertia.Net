import path from 'node:path'
import { defineConfig } from '@playwright/test'

// One spec suite, three samples: SAMPLE=react|vue|svelte (see samples/README.md for the contract they all implement).
const samples = {
  react: { dir: 'MinimalApi.React', port: 5101 },
  vue: { dir: 'Mvc.Vue', port: 5102 },
  svelte: { dir: 'FastEndpoints.Svelte', port: 5103 },
}
const name = (process.env.SAMPLE ?? 'react') as keyof typeof samples
const sample = samples[name]
if (!sample) throw new Error(`Unknown SAMPLE '${name}'. Use react, vue or svelte.`)

const baseURL = `http://localhost:${sample.port}`
const sampleDir = path.resolve(import.meta.dirname, '../../samples', sample.dir)

// The frontend must already be built (npm run build in the sample; INERTIA_SSR=1 also builds the SSR bundle).
export default defineConfig({
  testDir: './specs',
  workers: 1, // the samples keep in-memory state that the specs reset
  forbidOnly: !!process.env.CI,
  retries: process.env.CI ? 1 : 0,
  reporter: process.env.CI ? [['github'], ['html', { open: 'never' }]] : 'list',
  use: { baseURL, trace: 'retain-on-failure' },
  projects: [{ name: 'chromium', use: { browserName: 'chromium' } }],
  webServer: [
    {
      // With INERTIA_SSR=1 the react sample starts the Node SSR server itself (o.Ssr.UseNodeProcess()).
      command: `dotnet run --project "${sampleDir}" -c Release --no-launch-profile --urls ${baseURL}`,
      url: baseURL,
      env: { ...process.env, E2E: '1' } as Record<string, string>,
      reuseExistingServer: !process.env.CI,
      timeout: 120_000,
    },
  ],
})
