<script setup lang="ts">
import { InfiniteScroll } from '@inertiajs/vue3'
import Layout from '../../Layout.vue'

defineProps<{ posts: { data: { id: number; title: string }[] } }>()
</script>

<template>
  <Layout>
    <h1>Feed</h1>
    <InfiniteScroll data="posts">
      <article v-for="post in posts.data" :key="post.id" data-testid="post" style="height: 160px; border-bottom: 1px solid #ccc">{{ post.title }}</article>
    </InfiniteScroll>
  </Layout>
</template>
