import { expect, test } from '@playwright/test'

// React sample only, with the SSR bundle built and the SSR server running: INERTIA_SSR=1 SAMPLE=react.
test.skip(process.env.SAMPLE !== 'react' || process.env.INERTIA_SSR !== '1', 'SSR runs for the React sample with INERTIA_SSR=1')

test('the first visit is server-rendered and hydrates', async ({ request, page }) => {
  const html = await (await request.get('/users')).text()
  expect(html).toContain('data-server-rendered="true"')
  expect(html).toContain('data-testid="app-name"')

  await page.goto('/users')
  await expect(page.getByTestId('stats')).toBeVisible()
})
