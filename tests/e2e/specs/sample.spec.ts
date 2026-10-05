import { expect, test, type Page } from '@playwright/test'

// Runs against every sample (SAMPLE=react|vue|svelte); the contract is in samples/README.md.

test.beforeEach(async ({ request }) => {
  await request.post('/__e2e/reset')
})

const mark = (page: Page) => page.evaluate(() => ((window as any).__marker = 'alive'))
const marker = (page: Page) => page.evaluate(() => (window as any).__marker as string | undefined)

test('initial HTML embeds the page and the app renders', async ({ page, request }) => {
  const html = await (await request.get('/')).text()
  expect(html).toContain('<script data-page="app"')

  await page.goto('/')
  await expect(page.getByTestId('app-name')).toBeVisible()
  await expect(page.getByTestId('home-title')).toBeVisible()
})

test('Link navigation is an XHR visit, not a page load', async ({ page }) => {
  await page.goto('/')
  await mark(page)

  const visit = page.waitForRequest((r) => new URL(r.url()).pathname === '/users')
  await page.getByTestId('nav-users').click()
  expect((await visit).headers()['x-inertia']).toBe('true')

  await expect(page.getByTestId('user-row')).toHaveCount(3)
  await expect(page).toHaveURL(/\/users$/)
  expect(await marker(page)).toBe('alive')
})

test('form validation errors, then success redirect with flash', async ({ page }) => {
  await page.goto('/contacts/create')
  await page.getByTestId('submit').click()
  await expect(page.getByTestId('error-name')).toContainText(/required/i)
  await expect(page.getByTestId('error-email')).toContainText(/required/i)

  await page.getByTestId('name').fill('Ada')
  await page.getByTestId('email').fill('nope')
  await page.getByTestId('submit').click()
  await expect(page.getByTestId('error-email')).toContainText(/e-?mail/i)
  await expect(page.getByTestId('error-name')).toBeEmpty()

  await page.getByTestId('email').fill('ada@example.com')
  await page.getByTestId('submit').click()
  await expect(page).toHaveURL(/\/$/)
  await expect(page.getByTestId('home-title')).toBeVisible()
  await expect(page.getByTestId('flash')).toHaveText('Contact created')
})

test('error bag form keeps its errors apart from the default form', async ({ page }) => {
  await page.goto('/contacts/create')
  await page.getByTestId('newsletter-email').fill('not-an-email')
  await page.getByTestId('newsletter-submit').click()
  await expect(page.getByTestId('error-newsletter-email')).toContainText(/e-?mail/i)
  await expect(page.getByTestId('error-email')).toBeEmpty()
  await expect(page.getByTestId('error-name')).toBeEmpty()
})

test('deferred props load after the first render', async ({ page }) => {
  await page.goto('/users')
  await expect(page.getByTestId('stats-loading')).toBeVisible()
  await expect(page.getByTestId('stats')).toContainText('3')
  await expect(page.getByTestId('stats-loading')).toHaveCount(0)
})

test('once props are not resolved again on a second visit of the page', async ({ page }) => {
  await page.goto('/users')
  await expect(page.getByTestId('stats')).toBeVisible()
  await expect(page.getByTestId('plans-count')).toHaveText('1')

  // The client only reports the once props of the page it is on, so the second visit is Users -> Users.
  const revisit = page.waitForResponse((r) => new URL(r.url()).pathname === '/users' && r.request().headers()['x-inertia'] === 'true')
  await page.getByTestId('users-refresh').click()
  expect((await revisit).request().headers()['x-inertia-except-once-props']).toContain('plans')
  await expect(page.getByTestId('stats')).toBeVisible()
  await expect(page.getByTestId('plans-count')).toHaveText('1')

  // A full load resolves the prop again: 2 means the visit above did not (it would be 3).
  await page.reload()
  await expect(page.getByTestId('plans-count')).toHaveText('2')
})

test('infinite scroll appends the next page', async ({ page }) => {
  await page.goto('/feed')
  await expect(page.getByTestId('post')).toHaveCount(10)
  await expect
    .poll(async () => {
      await page.evaluate(() => window.scrollTo(0, document.body.scrollHeight))
      return page.getByTestId('post').count()
    })
    .toBeGreaterThan(10)
})

test('DELETE answers 303, flashes and removes the row', async ({ page }) => {
  await page.goto('/users')
  await expect(page.getByTestId('user-row')).toHaveCount(3)

  const statuses: number[] = []
  page.on('response', (r) => {
    if (r.request().method() === 'DELETE') statuses.push(r.status())
  })

  await page.getByTestId('delete-user-1').click()
  await expect(page.getByTestId('flash')).toHaveText('User deleted')
  await expect(page.getByTestId('user-row')).toHaveCount(2)
  expect(statuses).toEqual([303])
})

test('a version bump forces a full page reload on the next visit', async ({ page, request }) => {
  await page.goto('/')
  await mark(page)
  expect(await marker(page)).toBe('alive')

  await request.post('/__e2e/bump-version')

  const conflict = page.waitForResponse((r) => r.status() === 409)
  await page.getByTestId('nav-users').click()
  expect((await conflict).headers()['x-inertia-location']).toMatch(/\/users$/)

  await expect(page.getByTestId('user-row')).toHaveCount(3)
  expect(await marker(page)).toBeUndefined()
})
