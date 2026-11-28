<script lang="ts">
  import Layout from 'PARENTS/Layout.svelte'
  import type { InertiaPages } from 'PARENTS/types/inertia'

  let { message }: InertiaPages['PageName'] = $props()
</script>

<Layout>
  <h1>PageName</h1>
  <p>{message}</p>
</Layout>
