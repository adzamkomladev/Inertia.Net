<script setup lang="ts">
import { InfiniteScroll, router, useProp } from '@inertiajs/vue3'
import { onMounted, onUnmounted, ref, watchEffect } from 'vue'
import Layout from '../../Layout.vue'
import { liveReload } from '../../lib/inertia-live-reload'
import { live } from '../../lib/live'
import { liveEvents, routes } from '../../types/routes'

defineProps<{ posts: { data: { id: number; title: string }[] } }>()

// A live prop reads like any other; useProp also says when a live reload is in flight.
const { value: counter, loading: counterLoading } = useProp<{ value: number }>('counter')
const { value: inbox } = useProp<{ unread: number }>('inbox')
const hold = ref(false)
const held = ref(0)

// Returning false from the cancelable live event keeps the update back, e.g. to show a "new items" button instead.
watchEffect((onCleanup) => {
  if (!hold.value) return
  onCleanup(router.on('live', (event) => {
    if (!event.detail.props.includes('counter')) return
    held.value++
    return false
  }))
})

// posts is a scroll prop, which cannot be Live(): reload it when PostCreated arrives, replacing the pages already loaded.
let stopReload: (() => void) | undefined
onMounted(() => (stopReload = liveReload(live, liveEvents.PostCreated, { only: ['posts'], reset: ['posts'] })))
onUnmounted(() => stopReload?.())

const addPost = () => router.post(routes.live.posts.store().url, {}, { preserveScroll: true })
</script>

<template>
  <Layout>
    <h1>Live</h1>
    <p>
      Counter: <span data-testid="counter">{{ counter?.value }}</span>
      <span v-if="counterLoading" data-testid="counter-loading"> (updating...)</span>
    </p>
    <p>
      Unread messages: <span data-testid="inbox">{{ inbox?.unread }}</span>
    </p>
    <label><input v-model="hold" type="checkbox" data-testid="hold-updates" /> Hold updates</label>
    {{ ' ' }}<span v-if="hold" data-testid="held">{{ held }} held</span>
    <h2>Posts</h2>
    <button type="button" data-testid="add-post" @click="addPost">Add post</button>
    <InfiniteScroll data="posts">
      <article v-for="post in posts.data" :key="post.id" data-testid="live-post" style="height: 120px; border-bottom: 1px solid #ccc">
        {{ post.title }}
      </article>
    </InfiniteScroll>
  </Layout>
</template>
