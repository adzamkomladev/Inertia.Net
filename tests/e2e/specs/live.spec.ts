import { expect, test, type Page, type Request } from '@playwright/test'

// Live props over SignalR: the React sample's /live page (contract in samples/README.md).

test.skip((process.env.SAMPLE ?? 'react') !== 'react', 'The React sample hosts the live page')

test.beforeEach(async ({ page, request }) => {
  await request.post('/__e2e/reset')
  // Lets the app expose its hub connection as window.__inertiaLive.
  await page.addInitScript(() => ((window as any).__e2e = true))
})

/** Records hub invocations: the channels joined (completions with `true`) and left. */
const watchHub = (page: Page) => {
  const hub = { joined: 0, left: 0 }
  page.on('websocket', (socket) => {
    if (!socket.url().includes('/inertia-live')) return
    socket.on('framereceived', ({ payload }) => {
      hub.joined += String(payload).split('\x1e').filter((m) => /"type":3\b.*"result":true/.test(m)).length
    })
    socket.on('framesent', ({ payload }) => {
      hub.left += String(payload).split('\x1e').filter((m) => m.includes('"target":"Unsubscribe"')).length
    })
  })
  return hub
}

/** Partial reloads of the live props (other pages have deferred props of their own). */
const watchReloads = (page: Page) => {
  const reloads: Request[] = []
  page.on('request', (request) => {
    if (/\b(counter|inbox)\b/.test(request.headers()['x-inertia-partial-data'] ?? '')) reloads.push(request)
  })
  return reloads
}

/** Opens /live and waits until both of its channels (counter and the shared inbox) are joined. */
const openLive = async (page: Page) => {
  const hub = watchHub(page)
  const reloads = watchReloads(page)
  await page.goto('/live')
  await expect(page.getByTestId('counter')).toHaveText('0')
  await expect.poll(() => hub.joined).toBe(2)
  return { hub, reloads }
}

test('a publish without values reloads the live prop once', async ({ page, request }) => {
  const { reloads } = await openLive(page)

  await request.post('/__e2e/live/increment')

  await expect(page.getByTestId('counter')).toHaveText('1')
  expect(reloads.map((r) => r.headers()['x-inertia-partial-data'])).toEqual(['counter'])
  expect(reloads[0].headers()['x-socket-id']).toBeTruthy()
})

test('a publish with values updates the prop without a request', async ({ page, request }) => {
  const { reloads } = await openLive(page)

  await request.post('/__e2e/live/increment-with-value')

  await expect(page.getByTestId('counter')).toHaveText('1')
  await page.waitForTimeout(300) // nothing follows the update
  expect(reloads).toHaveLength(0)
})

test('a shared live prop follows its private channel', async ({ page, request }) => {
  await openLive(page)
  await expect(page.getByTestId('inbox')).toHaveText('0')

  await request.post('/__e2e/live/inbox')

  await expect(page.getByTestId('inbox')).toHaveText('1')
})

test('leaving the page leaves its channel, and coming back fetches a fresh value', async ({ page, request }) => {
  const { hub, reloads } = await openLive(page)

  await page.getByTestId('nav-users').click()
  await expect(page.getByTestId('user-row')).toHaveCount(3)
  await expect.poll(() => hub.left).toBe(1) // counter only: the shared inbox stays subscribed

  await request.post('/__e2e/live/increment')
  await page.waitForTimeout(300)
  expect(reloads).toHaveLength(0)

  await page.getByTestId('nav-live').click()
  await expect(page.getByTestId('counter')).toHaveText('1')
})

test('events missed while disconnected are caught up on reconnect', async ({ page, request }) => {
  await openLive(page)

  await page.evaluate(() => (window as any).__inertiaLive.stop())
  await expect.poll(() => page.evaluate(() => (window as any).__inertiaLive.state)).toBe('Disconnected')
  await request.post('/__e2e/live/increment')

  // The transport restarts the connection, joins its channels again and reports it; Inertia then reloads every live prop.
  await expect(page.getByTestId('counter')).toHaveText('1', { timeout: 15_000 })
  await expect.poll(() => page.evaluate(() => (window as any).__inertiaLive.state)).toBe('Connected')
})

test('a live listener that returns false holds the update back', async ({ page, request }) => {
  const { reloads } = await openLive(page)
  await page.getByTestId('hold-updates').check()

  await request.post('/__e2e/live/increment')

  await expect(page.getByTestId('held')).toHaveText('1 held')
  await page.waitForTimeout(300)
  await expect(page.getByTestId('counter')).toHaveText('0')
  expect(reloads).toHaveLength(0)
})
