import { expect, test } from '@playwright/test'

test('a generated route can open its destination before the server responds', async ({ page }) => {
  test.skip((process.env.SAMPLE ?? 'react') !== 'react', 'The React sample uses an instant home link.')

  const response = await page.goto('/users')
  expect(await response?.text()).toMatch(/<link rel="modulepreload"[^>]+href="\/build\/assets\/Index-[^"]+\.js">/)
  await expect(page.getByTestId('nav-home')).toBeVisible()

  const { promise: waitForRelease, resolve: release } = Promise.withResolvers<void>()
  const { promise: requested, resolve: markRequested } = Promise.withResolvers<void>()
  await page.route(url => url.pathname === '/', async route => {
    markRequested()
    await waitForRelease
    await route.continue()
  })

  try {
    await page.getByTestId('nav-home').click()
    await requested
    await expect(page.getByTestId('home-title')).toBeVisible()
    await expect(page.getByTestId('app-name')).toHaveText('Inertia.Net Sample')
  } finally {
    release()
  }

  await expect(page).toHaveURL(/\/$/)
})
