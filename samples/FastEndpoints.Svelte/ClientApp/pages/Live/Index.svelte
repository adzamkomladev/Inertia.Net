<script lang="ts">
  import { InfiniteScroll, router, useProp } from '@inertiajs/svelte'
  import Layout from '../../Layout.svelte'
  import { liveReload } from '../../lib/inertia-live-reload'
  import { live } from '../../lib/live'
  import { liveEvents, routes } from '../../types/routes'

  interface Counter {
    value: number
  }

  let { posts }: { posts: { data: { id: number; title: string }[] } } = $props()

  // A live prop reads like any other; useProp also says when a live reload is in flight.
  const counter = useProp<Counter>('counter')
  const inbox = useProp<{ unread: number }>('inbox')
  let hold = $state(false)
  let held = $state(0)

  // Returning false from the cancelable live event keeps the update back, e.g. to show a "new items" button instead.
  $effect(() => {
    if (!hold) return
    return router.on('live', (event) => {
      if (!event.detail.props.includes('counter')) return
      held++
      return false
    })
  })

  // posts is a scroll prop, which cannot be Live(): reload it when PostCreated arrives, replacing the pages already loaded.
  $effect(() => liveReload(live, liveEvents.PostCreated, { only: ['posts'], reset: ['posts'] }))
</script>

<Layout>
  <h1>Live</h1>
  <p>
    Counter: <span data-testid="counter">{counter.value?.value}</span>
    {#if counter.loading}<span data-testid="counter-loading"> (updating...)</span>{/if}
  </p>
  <p>
    Unread messages: <span data-testid="inbox">{inbox.value?.unread}</span>
  </p>
  <label>
    <input type="checkbox" data-testid="hold-updates" bind:checked={hold} /> Hold updates
  </label>
  {#if hold}<span data-testid="held">{held} held</span>{/if}
  <h2>Posts</h2>
  <button type="button" data-testid="add-post" onclick={() => router.post(routes.live.posts.store().url, {}, { preserveScroll: true })}>
    Add post
  </button>
  <InfiniteScroll data="posts">
    {#each posts.data as post (post.id)}
      <article data-testid="live-post" style="height: 120px; border-bottom: 1px solid #ccc">{post.title}</article>
    {/each}
  </InfiniteScroll>
</Layout>
