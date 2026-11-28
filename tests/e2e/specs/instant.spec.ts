import { expect, test } from '@playwright/test'

test('a generated route can open its destination before the server responds', async ({ page }) => {

  const response = await page.goto('/contacts/create')
  expect(await response?.text()).toMatch(/<link rel="modulepreload"[^>]+href="\/build\/assets\/Create-[^"]+\.js">/)
  await expect(page.getByRole('link', { name: 'Back to home' })).toBeVisible()

  const { promise: waitForRelease, resolve: release } = Promise.withResolvers<void>()
  const { promise: requested, resolve: markRequested } = Promise.withResolvers<void>()
  await page.route(url => url.pathname === '/', async route => {
    markRequested()
    await waitForRelease
    await route.continue()
  })

  const finalResponse = page.waitForResponse(response => new URL(response.url()).pathname === '/' && response.request().headers()['x-inertia'] === 'true')

  try {
    await page.getByRole('link', { name: 'Back to home' }).click()
    await requested
    await expect(page.getByTestId('home-title')).toBeVisible()
    await expect(page.getByTestId('app-name')).toHaveText('Inertia.Net Sample')
  } finally {
    release()
  }

  expect((await finalResponse).ok()).toBe(true)
  await expect(page).toHaveURL(/\/$/)
})
