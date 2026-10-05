<script lang="ts">
  import { InfiniteScroll } from '@inertiajs/svelte'
  import Layout from '../../Layout.svelte'

  let { posts }: { posts: { data: { id: number; title: string }[] } } = $props()
</script>

<Layout>
  <h1>Feed</h1>
  <InfiniteScroll data="posts">
    {#each posts.data as post (post.id)}
      <article data-testid="post" style="height: 160px; border-bottom: 1px solid #ccc">{post.title}</article>
    {/each}
  </InfiniteScroll>
</Layout>
