import { expect, test } from '@playwright/test'

// With the SSR bundle built (the sample starts the SSR server): INERTIA_SSR=1 SAMPLE=react|vue|svelte.
test.skip(process.env.INERTIA_SSR !== '1', 'SSR runs with INERTIA_SSR=1')

test('the first visit is server-rendered and hydrates', async ({ request, page }) => {
  const html = await (await request.get('/users')).text()
  expect(html).toContain('data-server-rendered="true"')
  expect(html).toContain('data-testid="app-name"')

  await page.goto('/users')
  await expect(page.getByTestId('stats')).toBeVisible()
})
