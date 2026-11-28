import { expect, test, type Page, type Request } from '@playwright/test'

// Live props over SignalR, or Server-Sent Events with LIVE_TRANSPORT=sse: each sample's /live page (contract in samples/README.md).


test.beforeEach(async ({ page, request }) => {
  await request.post('/__e2e/reset')
  // Lets the app expose its hub connection as window.__inertiaLive.
  await page.addInitScript(() => ((window as any).__e2e = true))
})

/**
 * Records the channels joined and left. SignalR: hub invocations (completions with `true`, Unsubscribe calls). Server-Sent Events
 * (LIVE_TRANSPORT=sse): the channels of each stream request, which reopens whenever the set changes.
 */
const watchHub = (page: Page) => {
  const hub = { joined: 0, left: 0 }
  if (process.env.LIVE_TRANSPORT === 'sse') {
    let current = new Set<string>()
    page.on('request', (request) => {
      const url = new URL(request.url())
      if (url.pathname !== '/inertia-live') return
      const next = new Set(url.searchParams.getAll('channel'))
      hub.left += [...current].filter((key) => !next.has(key)).length
      hub.joined = next.size
      current = next
    })
    return hub
  }

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

/** Opens /live and waits until its channels (counter, the shared inbox, and the posts feed's liveReload) are joined. */
const openLive = async (page: Page) => {
  const hub = watchHub(page)
  const reloads = watchReloads(page)
  await page.goto('/live')
  await expect(page.getByTestId('counter')).toHaveText('0')
  await expect.poll(() => hub.joined).toBe(3)
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

  const sse = process.env.LIVE_TRANSPORT === 'sse'
  if (sse) {
    // Publish after the old stream closes and before its replacement can subscribe.
    await page.route('**/inertia-live?*', async (route) => {
      await page.request.post('/__e2e/live/inbox')
      await route.continue()
    }, { times: 1 })
  }
  const catchUp = sse ? page.waitForResponse((response) =>
    response.request().headers()['x-inertia-partial-data'] === 'inbox') : undefined

  await page.getByTestId('nav-users').click()
  await expect(page.getByTestId('user-row')).toHaveCount(3)
  await expect.poll(() => hub.left).toBe(2) // counter and posts: the shared inbox stays subscribed

  if (catchUp) {
    const response = await catchUp
    expect((await response.json()).props.inbox.unread).toBe(1)
    reloads.length = 0
  }

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

/** Partial reloads of the posts feed, which liveReload() makes on PostCreated. */
const watchPostReloads = (page: Page) => {
  const reloads: Request[] = []
  page.on('request', (request) => {
    if (/\bposts\b/.test(request.headers()['x-inertia-partial-data'] ?? '')) reloads.push(request)
  })
  return reloads
}

const postTitles = (page: Page) => page.getByTestId('live-post').allTextContents()

test('liveReload brings a new post into a scroll prop without duplicates, keeping the scroll position', async ({ page, request }) => {
  const reloads = watchPostReloads(page)
  await openLive(page)
  await page.evaluate(() => window.scrollTo(0, 300))
  const scrollY = await page.evaluate(() => window.scrollY)

  await request.post('/__e2e/live/posts')

  await expect(page.getByTestId('live-post').first()).toHaveText('Live post 16')
  const titles = await postTitles(page)
  expect(new Set(titles).size).toBe(titles.length)
  expect(await page.evaluate(() => window.scrollY)).toBe(scrollY)
  expect(reloads).toHaveLength(1)
  expect(reloads[0].headers()['x-inertia-reset']).toBe('posts')
})

test('a burst of events becomes one reload', async ({ page, request }) => {
  const reloads = watchPostReloads(page)
  await openLive(page)

  await request.post('/__e2e/live/posts?count=20')

  await expect(page.getByTestId('live-post').first()).toHaveText('Live post 35')
  await page.waitForTimeout(500) // nothing follows the reload
  expect(reloads).toHaveLength(1)
})

test('the sender of an event is not reloaded by it', async ({ page }) => {
  const reloads = watchPostReloads(page)
  await openLive(page)

  await page.getByTestId('add-post').click()

  // The post's own visit brings it back; the broadcast leaves this connection out.
  await expect(page.getByTestId('live-post').first()).toHaveText('Live post 16')
  await page.waitForTimeout(500)
  expect(reloads).toHaveLength(0)
})
