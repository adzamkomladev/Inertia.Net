import { expect, test, type Request } from '@playwright/test'

test.skip((process.env.SAMPLE ?? 'react') !== 'react', 'The React sample hosts the pagination pages')

test.beforeEach(async ({ request }) => {
  await request.post('/__e2e/reset')
  await request.post('/__e2e/pagination/seed-users')
})

test('cursor scrolling loads every post in descending order through partial reloads', async ({ page }) => {
  const reloads: Request[] = []
  page.on('request', (request) => {
    if (new URL(request.url()).pathname === '/feed/cursor' && request.headers()['x-inertia-partial-data'] === 'posts') reloads.push(request)
  })
  await page.goto('/feed/cursor')
  await expect(page.getByTestId('post')).toHaveCount(10)
  await expect(page.getByTestId('post').first()).toHaveText('Post 50')
  await expect.poll(async () => {
    await page.evaluate(() => window.scrollTo(0, document.body.scrollHeight))
    return page.getByTestId('post').count()
  }, { timeout: 15_000 }).toBe(50)
  expect(reloads).toHaveLength(4)
  expect(reloads.every((request) => new URL(request.url()).searchParams.has('cursor'))).toBe(true)
  await expect(page.getByTestId('post').last()).toHaveText('Post 1')
})

test('numbered pager visits page two then one with only users and preserves state', async ({ page }) => {
  await page.goto('/users')
  await expect(page.getByTestId('user-row')).toHaveCount(5)
  await page.evaluate(() => ((window as any).__paginationMarker = 'alive'))
  const second = page.waitForRequest((request) => new URL(request.url()).searchParams.get('page') === '2')
  await page.getByTestId('page-2').click()
  expect((await second).headers()['x-inertia-partial-data']).toBe('users')
  await expect(page.getByTestId('user-row').first()).toContainText('User 6')
  await expect(page.getByTestId('page-2')).toHaveAttribute('aria-current', 'page')
  await page.getByTestId('page-1').click()
  await expect(page.getByTestId('user-row').first()).toContainText('Ada Lovelace')
  expect(await page.evaluate(() => (window as any).__paginationMarker)).toBe('alive')
})

test('a numbered page can be opened directly', async ({ page }) => {
  await page.goto('/users?page=3')
  await expect(page.getByTestId('user-row')).toHaveCount(2)
  await expect(page.getByTestId('user-row').first()).toContainText('User 11')
  await expect(page.getByTestId('page-3')).toHaveAttribute('aria-current', 'page')
})
