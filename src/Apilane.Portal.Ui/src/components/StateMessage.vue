<script setup lang="ts">
import type { Component } from 'vue'

// A centred message for when there is nothing to list: empty, not allowed, not found.
// Pass as="h1" when the message is the whole screen and there is no PageHeader above it.
withDefaults(
  defineProps<{
    icon: Component
    title: string
    description?: string
    as?: 'h1' | 'h2'
  }>(),
  { as: 'h2' },
)
</script>

<template>
  <div class="flex flex-col items-center rounded-lg border border-dashed px-6 py-14 text-center">
    <span class="flex size-10 items-center justify-center rounded-full bg-muted text-muted-foreground">
      <component :is="icon" class="size-5" />
    </span>
    <component :is="as" class="mt-4 text-sm font-medium">{{ title }}</component>
    <p v-if="description" class="mt-1 max-w-md text-sm text-muted-foreground">{{ description }}</p>
    <div v-if="$slots.default" class="mt-5 flex flex-wrap items-center justify-center gap-2">
      <slot />
    </div>
  </div>
</template>
