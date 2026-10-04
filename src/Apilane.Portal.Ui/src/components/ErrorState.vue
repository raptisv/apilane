<script setup lang="ts">
import { CircleAlertIcon, RefreshCwIcon } from '@lucide/vue'
import { computed } from 'vue'
import { Alert, AlertDescription, AlertTitle } from '@/components/ui/alert'
import { Button } from '@/components/ui/button'
import { ApiError } from '@/lib/api'
import ForbiddenState from './ForbiddenState.vue'

// What a screen shows when its data could not be loaded. Pair it with useAsync:
//   <ErrorState v-else-if="error" :error="error" @retry="reload" />
const props = defineProps<{ error: Error }>()
defineEmits<{ retry: [] }>()

const forbidden = computed(() => props.error instanceof ApiError && props.error.status === 403)
const traceId = computed(() => (props.error instanceof ApiError ? props.error.traceId : undefined))
</script>

<template>
  <ForbiddenState v-if="forbidden" as="h2" />

  <Alert v-else variant="destructive">
    <CircleAlertIcon />
    <AlertTitle>Could not load this page</AlertTitle>
    <AlertDescription>
      <p>{{ error.message }}</p>
      <p v-if="traceId" class="mt-1 font-mono text-xs opacity-80">Trace {{ traceId }}</p>
      <Button variant="outline" size="sm" class="mt-3" @click="$emit('retry')">
        <RefreshCwIcon />
        Try again
      </Button>
    </AlertDescription>
  </Alert>
</template>
