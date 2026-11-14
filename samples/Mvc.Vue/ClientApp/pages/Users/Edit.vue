<script setup lang="ts">
import { useForm } from '@inertiajs/vue3'
import { HeadlessModal } from '@inertiaui/modal-vue'
import { routes } from '../../types/routes'

// Opened from the users list with <ModalLink> (GET /users/{id}/edit returns Modal("Users/Edit", ...)). HeadlessModal leaves the
// styling, and the open/close transition, to the app; the styled <Modal> expects Tailwind CSS.
const props = defineProps<{ user: { id: number; name: string } }>()
const form = useForm({ name: props.user.name })
</script>

<template>
  <HeadlessModal v-slot="{ close, isOpen, afterLeave }">
    <Transition @after-leave="afterLeave">
      <div v-if="isOpen" role="dialog" aria-modal="true" aria-labelledby="edit-user-title" data-testid="user-modal"
        style="position: fixed; inset: 0; display: grid; place-items: center; background: rgb(0 0 0 / 0.4)">
        <form style="background: white; padding: 16px; border-radius: 8px; min-width: 280px"
          @submit.prevent="form.submit(routes.users.update(user.id), { onSuccess: () => close() })">
          <h2 id="edit-user-title">Edit {{ user.name }}</h2>
          <label>
            Name
            <input v-model="form.name" data-testid="user-name" />
          </label>
          <div data-testid="error-user-name" role="alert">{{ form.errors.name }}</div>
          <button type="submit" data-testid="user-save" :disabled="form.processing">Save</button>
          <button type="button" data-testid="user-cancel" @click="close">Cancel</button>
        </form>
      </div>
    </Transition>
  </HeadlessModal>
</template>
