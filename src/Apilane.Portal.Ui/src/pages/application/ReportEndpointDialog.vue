<script setup lang="ts">
import { nextTick, shallowRef, watch } from 'vue'
import CopyField from '@/components/CopyField.vue'
import { Button } from '@/components/ui/button'
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from '@/components/ui/dialog'
import { useApplication } from '@/composables/useApplication'
import { aggregateUrl, panelScope, seriesQuery } from '@/lib/reportData'
import type { Report } from '@/lib/reportData'

// 'View API endpoint' of a report panel: the address of the call each series makes to the API
// server, ready to copy. A series with a time range gets the window of the moment the dialog opens.
const props = defineProps<{ report: Report }>()

const open = defineModel<boolean>('open', { required: true })

const { application } = useApplication()

interface Endpoint {
  label: string
  url?: string
  /** Why the series has no address: it cannot run. */
  problem?: string
}

const endpoints = shallowRef<Endpoint[]>([])

function build(report: Report): Endpoint[] {
  const labels = panelScope(report).labels
  const now = new Date()

  return report.Series.map((series, index) => {
    const label = labels[index] ?? series.Label

    if (series.Error) {
      return { label, problem: series.Error }
    }

    try {
      return { label, url: aggregateUrl(application.value.Server.ServerUrl, application.value.Token, seriesQuery(report, series, now)) }
    } catch (error) {
      return { label, problem: error instanceof Error ? error.message : String(error) }
    }
  })
}

watch(
  [open, () => props.report],
  ([isOpen, report]) => {
    if (isOpen) {
      endpoints.value = build(report)
    }
  },
  { immediate: true },
)

// Opened from a menu item, the dialog has nothing to hand focus back to (the item is gone), so
// focus would drop to <body>: it goes to the screen instead.
async function onCloseAutoFocus(): Promise<void> {
  await nextTick()

  if (document.activeElement === document.body) {
    document.getElementById('main')?.focus({ preventScroll: true })
  }
}
</script>

<template>
  <Dialog v-model:open="open">
    <DialogContent class="max-h-[calc(100dvh-2rem)] overflow-y-auto sm:max-w-2xl" @close-auto-focus="onCloseAutoFocus">
      <DialogHeader class="pr-8">
        <DialogTitle>{{ endpoints.length > 1 ? 'API endpoints' : 'API endpoint' }}</DialogTitle>
        <DialogDescription>
          The call behind each series of '{{ report.Title }}'. Send it with your authorization token to get the same numbers.
        </DialogDescription>
      </DialogHeader>

      <p v-if="endpoints.length === 0" class="text-sm text-muted-foreground">This report has no series.</p>

      <template v-for="(endpoint, index) in endpoints" :key="index">
        <CopyField v-if="endpoint.url" :label="endpoint.label" :value="endpoint.url" />
        <div v-else class="grid gap-1.5">
          <p class="text-sm font-medium">{{ endpoint.label }}</p>
          <p class="text-sm text-destructive wrap-anywhere">{{ endpoint.problem }}</p>
        </div>
      </template>

      <DialogFooter>
        <Button type="button" @click="open = false">Close</Button>
      </DialogFooter>
    </DialogContent>
  </Dialog>
</template>
