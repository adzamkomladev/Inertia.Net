import { expect, test } from '@playwright/test'

test.beforeEach(async ({ request }) => {
  await request.post('/__e2e/reset')
})

for (const [path, status] of [['/nope', 404], ['/__e2e/boom', 500]] as const) {
  test(`an Inertia visit renders the ${status} page without the error modal`, async ({ page }) => {
    await page.goto('/')
    await page.evaluate(() => ((window as any).__marker = 'alive'))
    // Send an existing Inertia Link visit to the error route, without adding test-only links to the UI.
    await page.route('**/users', (route) => route.continue({ url: new URL(path, route.request().url()).href }))
    const response = page.waitForResponse((r) => r.request().headers()['x-inertia'] === 'true' && r.status() === status)
    await page.getByTestId('nav-users').click()
    expect((await response).headers()['x-inertia']).toBe('true')
    await expect(page.getByTestId('error-status')).toHaveText(String(status))
    await expect(page.getByTestId('app-name')).toBeVisible()
    await expect(page.locator('iframe, dialog')).toHaveCount(0)
    expect(await page.evaluate(() => (window as any).__marker)).toBe('alive')
    await page.getByRole('link', { name: 'Return home' }).click()
    await expect(page.getByTestId('home-title')).toBeVisible()
  })
}

test('a browser first visit renders the 404 page with its HTTP status', async ({ page }) => {
  const response = await page.goto('/nope')
  expect(response?.status()).toBe(404)
  await expect(page.getByTestId('error-status')).toHaveText('404')
  await expect(page.getByTestId('app-name')).toBeVisible()
  await expect(page.locator('iframe, dialog')).toHaveCount(0)
})
