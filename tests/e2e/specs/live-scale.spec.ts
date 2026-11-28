import { expect, test, type Request } from '@playwright/test'

// Live props across two instances of a sample on one backplane (LIVE_BACKPLANE=redis|postgres starts the second one at
// E2E_SECOND_URL): a publish on B reaches a page served by A, and a backplane gap ends in a reconnect and a reload.

const second = process.env.E2E_SECOND_URL
test.skip(!second, 'Needs LIVE_BACKPLANE')

test.beforeEach(async ({ page, request }) => {
  await request.post('/__e2e/reset')
  await request.post(`${second}/__e2e/reset`)
  await page.addInitScript(() => ((window as any).__e2e = true))
})

test('a publish on instance B updates a page on instance A, and a backplane gap reloads it', async ({ page, request }) => {
  const reloads: Request[] = []
  page.on('request', (r) => {
    if (/\bcounter\b/.test(r.headers()['x-inertia-partial-data'] ?? '')) reloads.push(r)
  })
  await page.goto('/live')
  await expect(page.getByTestId('counter')).toHaveText('0')

  // B's counter goes to 1 and its value travels with the event; A's page applies it without a request. Retried until A's stream has joined.
  await expect(async () => {
    await request.post(`${second}/__e2e/live/increment-with-value`)
    await expect(page.getByTestId('counter')).not.toHaveText('0', { timeout: 1_000 })
  }).toPass({ timeout: 15_000 })
  expect(reloads).toHaveLength(0)

  // Cutting the store's subscriptions makes A close its live connections; the client reconnects and reloads the prop from A (0).
  await request.post(`${second}/__e2e/live/backplane-gap`)
  await expect.poll(() => reloads.length, { timeout: 20_000 }).toBeGreaterThan(0)
  await expect(page.getByTestId('counter')).toHaveText('0')

  // And events flow again after the reconnect.
  await expect(async () => {
    await request.post(`${second}/__e2e/live/increment-with-value`)
    await expect(page.getByTestId('counter')).not.toHaveText('0', { timeout: 1_000 })
  }).toPass({ timeout: 20_000 })
})
