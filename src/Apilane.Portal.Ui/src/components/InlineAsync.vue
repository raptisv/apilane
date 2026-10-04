<script setup lang="ts">
import { RefreshCwIcon } from '@lucide/vue'
import { Button } from '@/components/ui/button'
import { Skeleton } from '@/components/ui/skeleton'

// A value that one row or one card loads on its own (a record count from the API server): a small
// skeleton while it loads, a short error with a retry when it fails, the default slot once it is
// there. The failure stays in its place; it never replaces the screen. Pair it with useAsync:
//
//   <InlineAsync :loading="counts.loading.value" :error="counts.error.value" label="the record count of Orders"
//     @retry="counts.reload()">{{ counts.data.value }}</InlineAsync>
defineProps<{
  loading: boolean
  error?: Error
  /** What is being loaded, for screen-reader users: 'Loading <label>'. */
  label: string
}>()
defineEmits<{ retry: [] }>()
</script>

<template>
  <span v-if="loading" role="status" class="inline-flex align-middle">
    <span class="sr-only">Loading {{ label }}</span>
    <Skeleton class="h-4 w-10" />
  </span>

  <span v-else-if="error" class="inline-flex max-w-full min-w-0 items-center gap-1 align-middle text-xs text-destructive">
    <span class="min-w-0 truncate" :title="error.message">{{ error.message }}</span>
    <Button variant="ghost" size="icon-xs" title="Try again" @click="$emit('retry')">
      <RefreshCwIcon />
      <span class="sr-only">Load {{ label }} again</span>
    </Button>
  </span>

  <slot v-else />
</template>
