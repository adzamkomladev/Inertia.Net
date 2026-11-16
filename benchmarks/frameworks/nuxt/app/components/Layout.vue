<!-- shared/ts/Layout.tsx in Vue: the same markup, with plain <a> links. -->
<script setup lang="ts">
defineProps<{ title: string; page: string }>()
</script>

<template>
  <div class="shell" :data-bench="page">
    <header>
      <a href="/" class="brand">Bench</a>
      <nav>
        <a href="/">Home</a> <a href="/todos?perPage=25">Todos</a> <a href="/todos/new">New todo</a> <a href="/dashboard">Dashboard</a>
        <a href="/compute?n=20000">Compute</a>
      </nav>
    </header>
    <main>
      <h1>{{ title }}</h1>
      <slot />
    </main>
  </div>
</template>
