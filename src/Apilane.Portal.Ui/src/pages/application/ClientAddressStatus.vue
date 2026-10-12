<script setup lang="ts">
import { CircleCheckIcon, InfoIcon, Loader2Icon, OctagonAlertIcon, TriangleAlertIcon } from '@lucide/vue'
import { computed, ref } from 'vue'
import { Button } from '@/components/ui/button'
import { useAsync } from '@/composables/useAsync'
import { apiServer } from '@/lib/apiServer'
import { assess } from '@/lib/clientAddress'
import type { ClientAddressInfo, Level } from '@/lib/clientAddress'
import ClientAddressDialog from './ClientAddressDialog.vue'

// What the API server of the application sees when this browser calls it, and what that means for the
// IP list: works, or what has to be set on the server. The browser asks the API server itself, as it
// does for the e-mail templates, so the answer is for the same road the callers of the application
// take (through the same proxy), as far as this browser is concerned.
//
//   <ClientAddressStatus :server-url="app.Server.ServerUrl" :app-token="app.Token" logic="Allow" :entries="['10.0.0.0/8']" />
const props = defineProps<{
  serverUrl: string
  appToken: string
  /** The saved list: what the API server enforces now. */
  logic: string
  entries: readonly string[]
}>()

const info = useAsync(() => apiServer(props.serverUrl, props.appToken).get<ClientAddressInfo>('/api/Application/GetClientAddress'))

const assessment = computed(() =>
  info.data.value ? assess(info.data.value, { logic: props.logic, entries: props.entries }) : undefined,
)

const detailsOpen = ref(false)

const styles: Record<Level, string> = {
  ok: 'border-success/40 bg-success/10 text-success',
  info: 'border-border bg-muted/40 text-foreground',
  warning: 'border-warning/40 bg-warning/10 text-warning',
  critical: 'border-destructive/40 bg-destructive/10 text-destructive',
}

const icons = { ok: CircleCheckIcon, info: InfoIcon, warning: TriangleAlertIcon, critical: OctagonAlertIcon }
</script>

<template>
  <div class="grid gap-2">
    <p v-if="info.loading.value" class="flex items-center gap-2 text-sm text-muted-foreground" role="status">
      <Loader2Icon class="size-4 animate-spin" aria-hidden="true" />
      Checking how the API server finds the address of a caller
    </p>

    <div
      v-else-if="assessment"
      class="grid gap-2 rounded-lg border p-3 text-sm"
      :class="styles[assessment.level]"
      role="status"
      data-testid="client-address-status"
    >
      <div class="flex gap-2.5">
        <component :is="icons[assessment.level]" class="mt-0.5 size-4 shrink-0" aria-hidden="true" />
        <div class="grid gap-1.5">
          <p class="font-medium">{{ assessment.title }}</p>
          <p class="text-foreground">{{ assessment.detail }}</p>
          <ol v-if="assessment.steps.length" class="list-decimal space-y-0.5 pl-5 text-foreground">
            <li v-for="step in assessment.steps" :key="step">{{ step }}</li>
          </ol>
        </div>
      </div>
    </div>

    <p v-else class="text-sm text-muted-foreground" role="status">
      The API server could not be asked how it finds the address of a caller, so nothing can be said about it here. The list
      works as saved.
    </p>

    <div class="flex flex-wrap gap-2">
      <Button type="button" variant="outline" size="sm" @click="detailsOpen = true">How the address is determined</Button>
      <Button type="button" variant="ghost" size="sm" :disabled="info.loading.value || info.refreshing.value" @click="info.reload()">
        Check again
      </Button>
    </div>

    <ClientAddressDialog v-model:open="detailsOpen" :info="info.data.value" :assessment="assessment" />
  </div>
</template>
