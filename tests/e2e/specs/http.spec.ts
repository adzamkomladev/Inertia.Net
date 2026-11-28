import { expect, test } from '@playwright/test'

// useHttp validation: the third form on /contacts/create posts JSON without X-Inertia, and the server's
// WithInertiaValidation() answers with a 422 whose errors useHttp puts in form.errors.


test('useHttp shows field errors from a 422 and clears them once the input is valid', async ({ page }) => {
  await page.goto('/contacts/create')
  const response = page.waitForResponse('**/contacts/check')
  await page.getByTestId('http-submit').click()
  expect((await response).status()).toBe(422)
  await expect(page.getByTestId('http-error-name')).not.toBeEmpty()
  await expect(page.getByTestId('http-error-email')).not.toBeEmpty()
  await expect(page).toHaveURL(/\/contacts\/create$/) // no visit

  await page.getByTestId('http-name').fill('Ann')
  await page.getByTestId('http-email').fill('ann@example.com')
  await page.getByTestId('http-submit').click()
  await expect(page.getByTestId('http-ok')).toBeVisible()
  await expect(page.getByTestId('http-error-name')).toBeEmpty()
})
