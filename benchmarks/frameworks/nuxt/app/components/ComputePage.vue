<script setup lang="ts">
defineProps<{ n: number; checksum: string; parallel: boolean }>()
</script>

<template>
  <Layout title="Compute" page="compute">
    <p>{{ n }} records{{ parallel ? ', split across cores' : '' }}</p>
    <p>Checksum <code :data-checksum="checksum">{{ checksum }}</code></p>
  </Layout>
</template>
