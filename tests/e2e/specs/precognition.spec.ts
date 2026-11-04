import { expect, test } from '@playwright/test'

test.skip((process.env.SAMPLE ?? 'react') !== 'react', 'The React sample hosts the login validation demo')

test.beforeEach(async ({ request }) => {
  await request.post('/__e2e/reset')
})

test('blur validation has its own bounded bucket and real submissions remain limited', async ({ page }) => {
  test.setTimeout(60_000)
  await page.goto('/login')
  const email = page.getByLabel('Email', { exact: true })
  const password = page.getByLabel('Password', { exact: true })
  const submit = page.getByRole('button', { name: 'Submit demo', exact: true })

  await email.fill('invalid')
  const invalid = page.waitForResponse(response => response.status() === 422
    && response.request().headers()['precognition-validate-only'] === 'email')
  await email.press('Tab')
  expect(await (await invalid).json()).toMatchObject({
    errors: { email: ['The Email field is not a valid e-mail address.'] },
  })
  await expect(page.getByRole('alert').filter({ hasText: 'not a valid e-mail address' })).toBeVisible()

  // More validations than the five-per-minute submit budget, all through the real form.
  for (let index = 0; index < 7; index++) {
    await email.fill(`person${index}@example.test`)
    const validation = page.waitForResponse(response =>
      new URL(response.url()).pathname === '/login'
      && response.request().method() === 'POST'
      && response.request().headers()['precognition'] === 'true'
      && response.request().headers()['precognition-validate-only'] === 'email')
    await email.press('Tab')
    const response = await validation
    expect(response.status()).toBe(204)
    expect(response.headers()['precognition']).toBe('true')
  }
  await expect(page.getByRole('alert').filter({ hasText: 'not a valid e-mail address' })).toHaveCount(0)
  await expect(page.getByTestId('login-submissions')).toHaveText('0')

  // Validation has not consumed any real submission permits.
  for (let count = 1; count <= 5; count++) {
    await password.fill('demo-password')
    const submission = page.waitForResponse(response =>
      new URL(response.url()).pathname === '/login' && response.request().method() === 'POST'
      && response.request().headers()['precognition'] !== 'true')
    await submit.click()
    expect((await submission).status()).toBe(302)
    await expect(page.getByTestId('login-submissions')).toHaveText(String(count))
  }

  // The sixth real submit is still rejected, as an Inertia error page without a Precognition header.
  await password.fill('demo-password')
  const rejected = page.waitForResponse(response =>
    new URL(response.url()).pathname === '/login' && response.status() === 429
    && response.request().headers()['precognition'] !== 'true')
  await submit.click()
  const response = await rejected
  expect(response.headers()['precognition']).toBeUndefined()
  await expect(page.getByTestId('error-status')).toHaveText('429')
  await expect(page.getByRole('heading', { name: '429: Too many requests' })).toBeVisible()

  // The validation bucket is bounded too, and its 429 keeps the protocol header.
  const validationRejection = await page.evaluate(async () => {
    const cookie = document.cookie.split('; ').find(value => value.startsWith('XSRF-TOKEN='))!
    const token = decodeURIComponent(cookie.slice('XSRF-TOKEN='.length))
    for (let count = 0; count < 40; count++) {
      const reply = await fetch('/login', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json', Accept: 'application/json', 'X-XSRF-TOKEN': token, Precognition: 'true', 'Precognition-Validate-Only': 'email' },
        body: JSON.stringify({ email: 'demo@example.test', password: 'demo-password' }),
      })
      if (reply.status === 429) return { status: reply.status, precognition: reply.headers.get('Precognition') }
      if (reply.status !== 204) throw new Error(`Unexpected validation status ${reply.status}`)
    }
    return null
  })
  expect(validationRejection).toEqual({ status: 429, precognition: 'true' })
})
