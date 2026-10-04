<script setup lang="ts">
import { Skeleton } from '@/components/ui/skeleton'

// The placeholder a screen shows while its first load runs. Pair it with useAsync:
//   <LoadingState v-if="loading" label="Loading servers" />
withDefaults(
  defineProps<{
    /** Read to screen-reader users; the grey rows say nothing to them. */
    label: string
    rows?: number
  }>(),
  { rows: 3 },
)
</script>

<template>
  <div role="status" class="space-y-2">
    <span class="sr-only">{{ label }}</span>
    <Skeleton v-for="n in rows" :key="n" class="h-11 w-full" />
  </div>
</template>
