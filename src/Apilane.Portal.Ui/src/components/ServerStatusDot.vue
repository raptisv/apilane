<script setup lang="ts">
import { computed } from 'vue'
import { useServerHealth } from '@/composables/useServerHealth'

// A dot that shows whether an API server answers: grey until the first answer, then green or red.
const props = defineProps<{ serverUrl: string }>()

const { online } = useServerHealth(() => props.serverUrl)

// Also the tooltip, so the state does not depend on telling the colours apart.
const label = computed(() => (online.value === undefined ? 'Checking' : online.value ? 'Online' : 'Offline'))
</script>

<template>
  <span class="inline-flex shrink-0 items-center" :title="label">
    <span
      class="size-2 rounded-full"
      :class="online === undefined ? 'bg-muted-foreground/40' : online ? 'bg-success' : 'bg-destructive'"
      aria-hidden="true"
    />
    <span class="sr-only">{{ label }}</span>
  </span>
</template>
