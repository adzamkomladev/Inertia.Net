<script lang="ts">
  import Index from './Index.svelte'

  let { posts }: { posts: { data: { id: number; title: string }[] } } = $props()
</script>

<Index {posts} />
