<script setup lang="ts">
// The title block of a screen: what it is, one line on what it is for, and its actions on the right.
defineProps<{
  title: string
  description?: string
}>()
</script>

<template>
  <div class="mb-6 flex flex-wrap items-start justify-between gap-x-6 gap-y-3">
    <div class="min-w-0">
      <h1 class="text-xl font-semibold tracking-tight">{{ title }}</h1>
      <p v-if="description" class="mt-1 text-sm text-muted-foreground">{{ description }}</p>
    </div>
    <div v-if="$slots.default" class="flex shrink-0 items-center gap-2">
      <slot />
    </div>
  </div>
</template>
