<script lang="ts">
  import Layout from '../Layout.svelte'
  import type { InertiaPages } from '../types/inertia'

  let { name }: InertiaPages['Dashboard'] = $props()
</script>

<Layout>
  <h1>Dashboard</h1>
  <p>You are signed in as {name}.</p>
</Layout>
