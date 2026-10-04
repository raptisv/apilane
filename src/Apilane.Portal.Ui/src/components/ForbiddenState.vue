<script setup lang="ts">
import { ShieldAlertIcon } from '@lucide/vue'
import StateMessage from './StateMessage.vue'

// Shown in place of a screen the user may not open. AppShell renders it for routes with
// meta.requiresAdmin, where it is the whole screen (h1); AppLayout renders it for routes with
// meta.requiresOwner and passes who may open it in `description`. ErrorState renders it under a
// PageHeader when the API answers 403 and passes as="h2".
withDefaults(defineProps<{ as?: 'h1' | 'h2'; description?: string }>(), {
  as: 'h1',
  description: 'It is available to administrators of this instance.',
})
</script>

<template>
  <StateMessage :icon="ShieldAlertIcon" :as="as" title="You do not have access to this page" :description="description" />
</template>
