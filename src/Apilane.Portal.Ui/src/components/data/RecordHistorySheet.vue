<script setup lang="ts">
import { HistoryIcon, Trash2Icon } from '@lucide/vue'
import { computed, ref, watch } from 'vue'
import ConfirmDialog from '@/components/ConfirmDialog.vue'
import ErrorState from '@/components/ErrorState.vue'
import LoadingState from '@/components/LoadingState.vue'
import StateMessage from '@/components/StateMessage.vue'
import { Button } from '@/components/ui/button'
import { Sheet, SheetContent, SheetDescription, SheetHeader, SheetTitle } from '@/components/ui/sheet'
import { useAsync } from '@/composables/useAsync'
import { useMutation } from '@/composables/useMutation'
import { apiServer } from '@/lib/apiServer'
import type { Entity } from '@/lib/entities'
import { formatCount } from '@/lib/entities'
import { cellText, historyLimit, historyRows } from '@/lib/records'
import type { DataApplication, HistoryEntry } from '@/lib/records'
import * as toast from '@/lib/toast'

// The change history of one record, in a side sheet: the newest 100 entries, each the record as it
// was before that change, with the values that changed since the entry below it marked. 'Clear
// history' removes the history of this record, after a confirm.
const props = defineProps<{
  application: DataApplication
  entity: Entity
  /** The primary key of the record. It may keep its value after the sheet closes. */
  recordId: unknown
}>()

const open = defineModel<boolean>('open', { required: true })

const server = computed(() => apiServer(props.application.Server.ServerUrl, props.application.Token))

// The columns: when, who, then the properties a caller may set.
const columns = computed(() => (props.entity.Properties ?? []).filter((property) => property.AllowEdit))

const history = useAsync(
  async () => {
    if (props.recordId === undefined) {
      return undefined
    }

    return server.value.get<{ Data: HistoryEntry[]; Total: number }>('/api/EntityHistory/Get', {
      entity: props.entity.Name,
      recordID: String(props.recordId),
      pageIndex: 1,
      pageSize: historyLimit,
    })
  },
  { watch: () => props.recordId },
)

const rows = computed(() => historyRows(history.data.value?.Data ?? [], columns.value))
const total = computed(() => history.data.value?.Total ?? 0)

// Reopening the same record fetches again: an edit or 'Clear history' may have changed it since.
watch(open, (isOpen) => {
  if (isOpen) {
    void history.reload()
  }
})

const clearOpen = ref(false)
const clear = useMutation(() =>
  server.value.delete('/api/EntityHistory/Delete', { entity: props.entity.Name, recordID: String(props.recordId) }),
)

watch(clearOpen, (isOpen) => isOpen && clear.reset())

async function submitClear(): Promise<boolean> {
  if (!(await clear.run())) {
    return false
  }

  toast.success('History of the record cleared.')
  void history.reload()
  return true
}
</script>

<template>
  <Sheet v-model:open="open">
    <SheetContent class="w-full gap-0 data-[side=right]:w-full data-[side=right]:sm:max-w-3xl">
      <SheetHeader class="border-b pr-12">
        <SheetTitle>History of record {{ String(recordId ?? '') }}</SheetTitle>
        <SheetDescription>
          Each line is the record as it was before one change, newest first. A marked value differs from the line
          below it.
        </SheetDescription>
      </SheetHeader>

      <div class="flex min-h-0 flex-1 flex-col gap-3 overflow-y-auto p-4">
        <LoadingState v-if="history.loading.value" label="Loading the history" />

        <ErrorState v-else-if="history.error.value" :error="history.error.value" @retry="history.reload()" />

        <StateMessage v-else-if="rows.length === 0" :icon="HistoryIcon" title="No history records found" />

        <template v-else>
          <div class="flex flex-wrap items-center justify-between gap-2">
            <p class="text-sm text-muted-foreground">
              <template v-if="total > rows.length">The newest {{ rows.length }} of {{ formatCount(total) }} entries.</template>
              <template v-else>{{ formatCount(total) }} {{ total === 1 ? 'entry' : 'entries' }}.</template>
            </p>
            <Button variant="outline" size="sm" @click="clearOpen = true">
              <Trash2Icon />
              Clear history
            </Button>
          </div>

          <div class="relative overflow-x-auto rounded-lg border">
            <table class="w-full text-sm">
              <thead class="border-b bg-muted/30 text-left">
                <tr>
                  <th class="px-2 py-2 font-medium whitespace-nowrap">Timestamp</th>
                  <th class="px-2 py-2 font-medium whitespace-nowrap">Owner</th>
                  <th v-for="column in columns" :key="column.Name" class="px-2 py-2 font-medium whitespace-nowrap">
                    {{ column.Name }}
                  </th>
                </tr>
              </thead>
              <tbody>
                <tr v-for="row in rows" :key="row.id" class="border-b last:border-b-0">
                  <td class="px-2 py-1.5 font-mono text-xs whitespace-nowrap">{{ row.timestamp }}</td>
                  <td class="px-2 py-1.5 font-mono text-xs whitespace-nowrap">
                    <template v-if="row.owner !== null">{{ row.owner }}</template>
                    <span v-else class="text-muted-foreground italic">null</span>
                  </td>
                  <td
                    v-for="column in columns"
                    :key="column.Name"
                    class="max-w-64 truncate px-2 py-1.5"
                    :class="row.changed.has(column.Name) && 'bg-warning/10 text-warning'"
                    :title="cellText(column, row.values[column.Name]) ?? 'null'"
                  >
                    <template v-if="cellText(column, row.values[column.Name]) !== null">{{ cellText(column, row.values[column.Name]) }}</template>
                    <span v-else class="text-muted-foreground italic">null</span>
                    <span v-if="row.changed.has(column.Name)" class="sr-only">(changed)</span>
                  </td>
                </tr>
              </tbody>
            </table>
          </div>
        </template>
      </div>
    </SheetContent>
  </Sheet>

  <ConfirmDialog
    v-model:open="clearOpen"
    title="Clear record history"
    :description="`Clear the history of record ${String(recordId ?? '')} of ${entity.Name}? This cannot be undone.`"
    confirm-label="Clear history"
    destructive
    :action="submitClear"
    :error="clear.error.value?.message"
  />
</template>
