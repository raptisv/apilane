<script setup lang="ts">
import { watch } from 'vue'
import { useRoute } from 'vue-router'
import { useInstance } from '@/composables/useInstance'
import { setTitle } from '@/router'

// The layout of the screens that work without a session (routes with meta.public): one centred
// card with the instance name above the screen. It must not use useSession(): nobody may be signed in.
const route = useRoute()
const { data } = useInstance()

const logo = '/favicon.ico'

// The tab title carries the instance name, which is known only once GET /instance has answered.
watch(data, () => setTitle(route.meta.title))
</script>

<template>
  <div class="flex min-h-dvh items-center justify-center p-4">
    <main class="w-full max-w-sm rounded-lg border bg-card p-6 sm:p-8">
      <!-- The name falls back to 'Apilane' while it loads or when it cannot be loaded: the screen still works. -->
      <p class="mb-6 flex items-center justify-center gap-2.5 text-lg font-semibold">
        <img :src="logo" alt="" class="size-6 shrink-0 rounded" />
        <span class="truncate">{{ data?.InstanceTitle ?? 'Apilane' }}</span>
      </p>

      <RouterView />
    </main>
  </div>
</template>
