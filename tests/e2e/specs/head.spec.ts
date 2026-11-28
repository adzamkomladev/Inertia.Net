import { expect, test } from '@playwright/test'

// /users sets its title and description on the server with Render(...).WithHead(...) (serverHead: true).

test.beforeEach(async ({ request }) => {
  await request.post('/__e2e/reset')
})

const description = 'meta[name="description"][data-inertia="description"]'

test.describe('without JavaScript', () => {
  test.use({ javaScriptEnabled: false })

  test('the first load has the server head tags', async ({ page }) => {
    await page.goto('/users')
    await expect(page).toHaveTitle('Users - Inertia.Net Sample')
    await expect(page.locator(description)).toHaveAttribute('content', 'Paginated users with deferred stats')
  })
})

test('the client keeps the server head up to date across visits', async ({ page }) => {
  await page.goto('/users')
  await expect(page).toHaveTitle('Users - Inertia.Net Sample')
  await expect(page.locator(description)).toHaveCount(1)
  await expect(page.locator('head title')).toHaveCount(1)

  await page.getByTestId('nav-home').click()
  await expect(page).toHaveURL(/\/$/)
  await expect(page.locator(description)).toHaveCount(0)

  await page.getByTestId('nav-users').click()
  await expect(page).toHaveTitle('Users - Inertia.Net Sample')
  await expect(page.locator(description)).toHaveAttribute('content', 'Paginated users with deferred stats')
})
