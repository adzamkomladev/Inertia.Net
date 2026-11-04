import { expect, test } from '@playwright/test'

test('validation redirects back when the browser sends no Referer', async ({ page, request }) => {
  await request.post('/__e2e/reset')
  await page.route('**/*', async (route) => {
    if (route.request().resourceType() !== 'document') return route.continue()
    const response = await route.fetch()
    await route.fulfill({ response, headers: { ...response.headers(), 'referrer-policy': 'no-referrer' } })
  })
  await page.goto('/contacts/create')
  await page.getByTestId('name').fill('Ada')
  const submitted = page.waitForRequest((r) => new URL(r.url()).pathname === '/contacts' && r.method() === 'POST')
  await page.getByTestId('submit').click()
  expect((await submitted).headers()['referer']).toBeUndefined()
  await expect(page).toHaveURL(/\/contacts\/create$/)
  await expect(page.getByTestId('error-email')).toContainText(/required/i)
  await expect(page.getByTestId('name')).toHaveValue('Ada')
})
