<script lang="ts">
  import { Link } from '@inertiajs/svelte'
  import Layout from '../Layout.svelte'
  import type { InertiaPages } from '../types/inertia'

  let { message }: InertiaPages['Home'] = $props()
</script>

<Layout>
  <h1>Home</h1>
  <p>{message}</p>
  <Link href="/todos">See the todos</Link>
</Layout>
