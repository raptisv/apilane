<script setup lang="ts">
import type { Consequence } from '@/lib/applicationActions'

// The bulleted consequences of an action that cannot be undone (rebuild, delete an application),
// in its dialog and on the settings screen. The lines come from lib/applicationActions.ts.
defineProps<{ items: readonly Consequence[] }>()
</script>

<template>
  <ul class="grid list-disc gap-1 pl-5 text-sm text-muted-foreground">
    <li v-for="item in items" :key="item.text" :class="item.strong && 'font-semibold text-foreground'">{{ item.text }}</li>
  </ul>
</template>
