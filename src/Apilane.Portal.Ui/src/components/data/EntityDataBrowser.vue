<script setup lang="ts">
import {
  ArrowDownIcon,
  ArrowUpDownIcon,
  ArrowUpIcon,
  CircleAlertIcon,
  DownloadIcon,
  EllipsisIcon,
  FileSpreadsheetIcon,
  HistoryIcon,
  InfoIcon,
  Loader2Icon,
  PencilIcon,
  PlusIcon,
  RefreshCwIcon,
  Trash2Icon,
  UploadIcon,
  XIcon,
} from '@lucide/vue'
import { computed, onBeforeUnmount, ref, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import ConfirmDialog from '@/components/ConfirmDialog.vue'
import LoadingState from '@/components/LoadingState.vue'
import { Button } from '@/components/ui/button'
import { Checkbox } from '@/components/ui/checkbox'
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuTrigger,
} from '@/components/ui/dropdown-menu'
import { Tooltip, TooltipContent, TooltipTrigger } from '@/components/ui/tooltip'
import { useAsync } from '@/composables/useAsync'
import { useMutation } from '@/composables/useMutation'
import { usePageQuery } from '@/composables/usePageQuery'
import { apiServer } from '@/lib/apiServer'
import { csvFileName, toCsv } from '@/lib/csv'
import { saveBlob } from '@/lib/download'
import type { Entity } from '@/lib/entities'
import { propertyRules } from '@/lib/properties'
import type { Property } from '@/lib/properties'
import {
  cellText,
  emptyFilters,
  filterParam,
  hasFilters,
  isFiles,
  nextSort,
  pageSizeFrom,
  primaryKey,
  recordActions,
  sortFrom,
  sortParam,
  sortQuery,
} from '@/lib/records'
import type { DataApplication, DataRecord, RecordPage } from '@/lib/records'
import * as toast from '@/lib/toast'
import FileUploadDialog from './FileUploadDialog.vue'
import RecordFilterInput from './RecordFilterInput.vue'
import RecordFormSheet from './RecordFormSheet.vue'
import RecordHistorySheet from './RecordHistorySheet.vue'
import RecordPaging from './RecordPaging.vue'

// The records of one entity: a grid with a sort per column, a filter per column, paging, row
// actions, the record form, the history and, for Files, upload and download. It replaces the
// classic Views/Entity/Data.cshtml and follows its rules (see lib/records.ts). The records are read
// and written on the API server, straight from the browser (lib/apiServer.ts).
//
// Give it a :key of the entity name, so another entity gets a fresh grid. The page (?page=), page
// size (?pageSize=) and sort (?sort=Name or ?sort=-Name) are in the address; the filters are not.
// ApplicationDataBrowser hosts it, for the application's data browser and the administrator's.
const props = defineProps<{
  application: DataApplication
  /** The entity, with its Properties. */
  entity: Entity
  /**
   * The application is someone else's: an administrator browsing an application they neither own
   * nor collaborate on. Links to its screens under /apps, which that user cannot open, are left out.
   */
  foreign?: boolean
}>()

const route = useRoute()
const router = useRouter()

// The component is keyed by the entity, so these are fixed for its life.
const server = apiServer(props.application.Server.ServerUrl, props.application.Token)
const entityName = props.entity.Name
const files = isFiles(props.entity)
// The classic page shows the columns, the form and the history in creation order, not in the
// order of the properties page the API sends.
const ordered = computed<Entity>(() => ({
  ...props.entity,
  Properties: [...(props.entity.Properties ?? [])].sort((a, b) => a.Position - b.Position),
}))
const properties = computed(() => ordered.value.Properties ?? [])
const key = computed(() => primaryKey(properties.value))
const actions = computed(() => recordActions(props.entity))
const anyRowAction = computed(() => actions.value.edit || actions.value.delete || actions.value.download || actions.value.history)

// Address state
const page = usePageQuery()
const pageSize = computed(() => pageSizeFrom(route.query.pageSize))
const sort = computed(() => sortFrom(route.query.sort, properties.value))

function query(changes: Record<string, string | undefined>) {
  const next: Record<string, unknown> = { ...route.query, ...changes }

  for (const name of Object.keys(next)) {
    if (next[name] === undefined) {
      delete next[name]
    }
  }

  return { query: next as Record<string, string> }
}

function toggleSort(property: string): void {
  void router.push(query({ sort: sortQuery(nextSort(sort.value, property)) }))
}

// Filters: what is typed is sent once typing pauses. A newer request cancels the one still running,
// so a slow old answer can never replace a newer one.
const filters = ref(emptyFilters(properties.value))
const appliedFilter = ref<string>()
const filtering = computed(() => hasFilters(filters.value))
let filterTimer: ReturnType<typeof setTimeout> | undefined

function applyFilters(): void {
  clearTimeout(filterTimer)
  appliedFilter.value = filterParam(properties.value, filters.value)
}

watch(
  filters,
  () => {
    clearTimeout(filterTimer)
    filterTimer = setTimeout(applyFilters, 400)
  },
  { deep: true },
)

function clearFilters(): void {
  filters.value = emptyFilters(properties.value)
  applyFilters()
}

let controller: AbortController | undefined

const records = useAsync(() => {
  controller?.abort()
  controller = new AbortController()

  return server.get<RecordPage>(
    files ? '/api/Files/Get' : '/api/Data/Get',
    {
      entity: files ? undefined : entityName,
      pageIndex: page.value,
      pageSize: pageSize.value,
      filter: appliedFilter.value,
      sort: sortParam(sort.value),
      getTotal: true,
    },
    { signal: controller.signal },
  )
})

onBeforeUnmount(() => {
  controller?.abort()
  clearTimeout(filterTimer)
})

watch([page, pageSize, () => sortQuery(sort.value)], () => void records.reload())

// A new filter starts again at page 1 (the address change then reloads).
watch(appliedFilter, () => {
  if (page.value !== 1) {
    void router.replace(query({ page: undefined }))
  } else {
    void records.reload()
  }
})

/** After a create, an edit or a delete the grid starts again at page 1, as in the classic page. */
function reloadFromFirstPage(): void {
  if (page.value !== 1) {
    void router.push(query({ page: undefined }))
  } else {
    void records.reload()
  }
}

const rows = computed(() => records.data.value?.Data ?? [])
const total = computed(() => records.data.value?.Total ?? 0)
const columnCount = computed(() => properties.value.length + 1)

function idOf(row: DataRecord): string {
  return String(row[key.value] ?? '')
}

// A cell is cut to one line; a click shows all of it, a second click cuts it again.
const expanded = ref(new Set<string>())

function toggleCell(row: DataRecord, property: string): void {
  const cell = `${idOf(row)}:${property}`
  const next = new Set(expanded.value)

  if (!next.delete(cell)) {
    next.add(cell)
  }

  expanded.value = next
}

// Selection, for deleting several records at once.
const selected = ref(new Set<string>())

watch(records.data, () => {
  selected.value = new Set()
  expanded.value = new Set()
})

const allSelected = computed(() => rows.value.length > 0 && rows.value.every((row) => selected.value.has(idOf(row))))
const someSelected = computed(() => !allSelected.value && rows.value.some((row) => selected.value.has(idOf(row))))

function toggleRow(row: DataRecord): void {
  const next = new Set(selected.value)

  if (!next.delete(idOf(row))) {
    next.add(idOf(row))
  }

  selected.value = next
}

function toggleAll(): void {
  selected.value = allSelected.value ? new Set() : new Set(rows.value.map(idOf))
}

// Create and edit. `editing` keeps its value after the sheet closes, so its text does not change
// while it slides out.
const formOpen = ref(false)
const editing = ref<DataRecord>()
const uploadOpen = ref(false)

function openCreate(): void {
  if (files) {
    uploadOpen.value = true
    return
  }

  editing.value = undefined
  formOpen.value = true
}

function openEdit(row: DataRecord): void {
  editing.value = row
  formOpen.value = true
}

function onSaved(): void {
  toast.success(editing.value ? 'Record saved.' : actions.value.createLabel === 'Register user' ? 'User registered.' : 'Record created.')
  reloadFromFirstPage()
}

function onUploaded(): void {
  toast.success('File uploaded.')
  // The classic page stays on the same page after an upload.
  void records.reload()
}

// Delete, one record or the selected ones.
const deleteOpen = ref(false)
const deleting = ref<string[]>([])

const remove = useMutation((ids: string[]) =>
  server.delete<number[]>(files ? '/api/Files/Delete' : '/api/Data/Delete', {
    entity: files ? undefined : entityName,
    ids: ids.join(','),
  }),
)

function openDelete(ids: string[]): void {
  deleting.value = ids
  remove.reset()
  deleteOpen.value = true
}

// The ids are listed for a small selection only, so a whole page of 1000 does not push the buttons off screen.
const deleteText = computed(() => {
  const ids = deleting.value

  if (ids.length === 1) {
    return `Delete record ${ids[0]} of ${entityName}? This cannot be undone.`
  }

  const list = ids.length <= 10 ? ` (${ids.join(', ')})` : ''
  return `Delete the ${ids.length} selected records of ${entityName}${list}? This cannot be undone.`
})

async function submitDelete(): Promise<boolean> {
  if (!(await remove.run(deleting.value))) {
    return false
  }

  toast.success(deleting.value.length === 1 ? 'Record deleted.' : `${deleting.value.length} records deleted.`)
  reloadFromFirstPage()
  return true
}

// History of one record, in a sheet. `historyId` keeps its value after the sheet closes, so its
// title and rows do not change while it slides out.
const historyOpen = ref(false)
const historyId = ref<string>()

function openHistory(id: string): void {
  historyId.value = id
  historyOpen.value = true
}

// History of the whole entity.
const clearHistoryOpen = ref(false)
const clearHistory = useMutation(() => server.delete('/api/EntityHistory/Delete', { entity: entityName }))

function openClearHistory(): void {
  clearHistory.reset()
  clearHistoryOpen.value = true
}

async function submitClearHistory(): Promise<boolean> {
  if (!(await clearHistory.run())) {
    return false
  }

  toast.success(`History of ${entityName} cleared.`)
  void records.reload()
  return true
}

// Files: the download is fetched with the token in a header (never in the address) and saved.
// The ids of the files still loading: several can load at once.
const downloading = ref(new Set<string>())

async function download(row: DataRecord): Promise<void> {
  const id = idOf(row)
  downloading.value.add(id)

  try {
    const blob = await server.getBlob('/api/Files/Download', { fileID: id })
    saveBlob(blob, typeof row.Name === 'string' && row.Name !== '' ? row.Name : `file-${id}`)
  } catch (error) {
    toast.error(error)
  } finally {
    downloading.value.delete(id)
  }
}

// CSV of the page on screen, the values as the grid shows them.
function exportCsv(): void {
  const headers = properties.value.map((property) => property.Name)
  const lines = rows.value.map((row) => properties.value.map((property) => cellText(property, row[property.Name])))

  saveBlob(new Blob([toCsv(headers, lines)], { type: 'text/csv;charset=utf-8' }), csvFileName(entityName, new Date()))
}

function about(property: Property): string[] {
  const kind = [property.Type, property.IsPrimaryKey && 'primary key', property.Required && 'required', property.Encrypted && 'encrypted']
    .filter(Boolean)
    .join(', ')
  const dates = property.Type === 'Date' ? [property.IsUtc ? 'Shown in your local time. Filter in UTC.' : 'Shown in UTC.'] : []

  return [kind, ...propertyRules(property), ...dates, ...(property.Description ? [property.Description] : [])]
}

function ariaSort(property: string): 'ascending' | 'descending' | undefined {
  return sort.value?.Property === property ? (sort.value.Direction === 'asc' ? 'ascending' : 'descending') : undefined
}

function sortLabel(property: string): string {
  const direction = ariaSort(property)

  return direction === 'ascending'
    ? `Sorted by ${property}, ascending. Sort descending.`
    : direction === 'descending'
      ? `Sorted by ${property}, descending. Remove the sort.`
      : `Sort by ${property}`
}

const headClass = 'px-2 py-2 text-left align-middle font-medium whitespace-nowrap'
const cellClass = 'px-2 py-1.5 align-middle'
</script>

<template>
  <div class="flex flex-wrap items-center gap-2">
    <Button variant="outline" size="sm" :disabled="records.refreshing.value" @click="records.reload()">
      <RefreshCwIcon :class="records.refreshing.value && 'animate-spin'" />
      Refresh
    </Button>
    <Button v-if="actions.create" size="sm" @click="openCreate">
      <component :is="files ? UploadIcon : PlusIcon" />
      {{ actions.createLabel }}
    </Button>
    <Button v-if="actions.delete && selected.size > 0" variant="destructive" size="sm" @click="openDelete([...selected])">
      <Trash2Icon />
      Delete selected ({{ selected.size }})
    </Button>

    <DropdownMenu>
      <DropdownMenuTrigger as-child>
        <Button variant="outline" size="icon-sm" title="More actions">
          <EllipsisIcon />
          <span class="sr-only">More actions for {{ entityName }}</span>
        </Button>
      </DropdownMenuTrigger>
      <DropdownMenuContent align="start" class="w-52">
        <DropdownMenuItem :disabled="rows.length === 0" @select="exportCsv">
          <FileSpreadsheetIcon />
          Export page as CSV
        </DropdownMenuItem>
        <DropdownMenuItem v-if="entity.RequireChangeTracking" variant="destructive" @select="openClearHistory">
          <HistoryIcon />
          Clear history
        </DropdownMenuItem>
      </DropdownMenuContent>
    </DropdownMenu>
  </div>

  <!--
    The grid scrolls inside its frame, both ways, so a wide entity never widens the page; the header
    and filter rows stay at the top. `relative` keeps the sr-only texts inside the frame.
  -->
  <div class="relative mt-3 max-h-[70vh] overflow-auto rounded-lg border bg-card" :aria-busy="records.refreshing.value">
    <table class="w-full text-sm">
      <caption class="sr-only">Records of {{ entityName }}</caption>
      <thead class="sticky top-0 z-10 bg-card">
        <tr class="border-b">
          <th :class="headClass" class="w-px">
            <div class="flex items-center gap-2">
              <Checkbox
                v-if="actions.delete && rows.length > 0"
                :model-value="allSelected ? true : someSelected ? 'indeterminate' : false"
                aria-label="Select all records of this page"
                @update:model-value="toggleAll"
              />
              <span class="sr-only">Actions</span>
            </div>
          </th>
          <th v-for="property in properties" :key="property.Name" :class="headClass" :aria-sort="ariaSort(property.Name)">
            <div class="flex items-center gap-1 whitespace-nowrap">
              <button
                type="button"
                class="inline-flex items-center gap-1 rounded-sm hover:text-foreground focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring"
                :title="sortLabel(property.Name)"
                @click="toggleSort(property.Name)"
              >
                {{ property.Name }}
                <ArrowUpIcon v-if="ariaSort(property.Name) === 'ascending'" class="size-3.5 text-link" aria-hidden="true" />
                <ArrowDownIcon v-else-if="ariaSort(property.Name) === 'descending'" class="size-3.5 text-link" aria-hidden="true" />
                <ArrowUpDownIcon v-else class="size-3.5 text-muted-foreground/60" aria-hidden="true" />
                <span class="sr-only">{{ sortLabel(property.Name) }}</span>
              </button>
              <Tooltip>
                <TooltipTrigger as-child>
                  <button
                    type="button"
                    class="rounded-sm text-muted-foreground hover:text-foreground focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring"
                  >
                    <InfoIcon class="size-3.5" aria-hidden="true" />
                    <span class="sr-only">About {{ property.Name }}: {{ about(property).join('; ') }}</span>
                  </button>
                </TooltipTrigger>
                <TooltipContent class="flex-col items-start gap-0.5 font-normal">
                  <span v-for="(line, index) in about(property)" :key="index" class="wrap-anywhere whitespace-normal">{{ line }}</span>
                </TooltipContent>
              </Tooltip>
            </div>
          </th>
        </tr>
        <tr class="border-b">
          <td :class="cellClass" class="py-2.5">
            <Button v-if="filtering" variant="ghost" size="xs" title="Clear filters" @click="clearFilters">
              <XIcon />
              Clear
              <span class="sr-only">filters</span>
            </Button>
          </td>
          <td v-for="property in properties" :key="property.Name" :class="cellClass" class="py-2.5">
            <RecordFilterInput v-model="filters[property.Name]" :property="property" />
          </td>
        </tr>
      </thead>

      <tbody class="transition-opacity" :class="records.refreshing.value && 'opacity-60'">
        <tr v-if="records.loading.value">
          <td :colspan="columnCount" class="p-3">
            <LoadingState label="Loading records" :rows="5" />
          </td>
        </tr>

        <tr v-else-if="records.error.value">
          <td :colspan="columnCount" class="p-0">
            <div role="alert" class="sticky left-0 flex max-w-xl gap-2.5 p-4 text-sm text-destructive">
              <CircleAlertIcon class="mt-0.5 size-4 shrink-0" aria-hidden="true" />
              <div>
                <p class="font-medium">Could not load the records</p>
                <p class="mt-0.5 wrap-anywhere text-destructive/90">{{ records.error.value.message }}</p>
                <Button variant="outline" size="sm" class="mt-3" @click="records.reload()">
                  <RefreshCwIcon />
                  Try again
                </Button>
              </div>
            </div>
          </td>
        </tr>

        <tr v-else-if="rows.length === 0">
          <td :colspan="columnCount" class="p-0">
            <div class="sticky left-0 max-w-xl p-4 text-sm">
              <template v-if="filtering">
                <p class="font-medium">No records match the filters</p>
                <Button variant="outline" size="sm" class="mt-3" @click="clearFilters">Clear filters</Button>
              </template>
              <p v-else-if="total > 0" class="font-medium">There are no records on this page.</p>
              <template v-else>
                <p class="font-medium">No records found</p>
                <p class="mt-0.5 text-muted-foreground">{{ entityName }} has no records yet.</p>
                <Button v-if="actions.create" size="sm" class="mt-3" @click="openCreate">
                  <component :is="files ? UploadIcon : PlusIcon" />
                  {{ actions.createLabel }}
                </Button>
              </template>
            </div>
          </td>
        </tr>

        <template v-else>
          <tr
            v-for="row in rows"
            :key="idOf(row)"
            class="border-b transition-colors last:border-b-0 hover:bg-muted/50"
            :class="selected.has(idOf(row)) && 'bg-muted/40'"
          >
            <td :class="cellClass" class="w-px">
              <!-- One line, never wrapping: the checkbox and the actions of the row. -->
              <div class="flex items-center gap-1 whitespace-nowrap">
                <Checkbox
                  v-if="actions.delete"
                  class="mr-1"
                  :model-value="selected.has(idOf(row))"
                  :aria-label="`Select record ${idOf(row)}`"
                  @update:model-value="toggleRow(row)"
                />
                <Button v-if="actions.history" variant="ghost" size="icon-xs" title="History" @click="openHistory(idOf(row))">
                  <HistoryIcon />
                  <span class="sr-only">History of record {{ idOf(row) }}</span>
                </Button>
                <Button
                  v-if="actions.download"
                  variant="ghost"
                  size="icon-xs"
                  title="Download"
                  :disabled="downloading.has(idOf(row))"
                  @click="download(row)"
                >
                  <Loader2Icon v-if="downloading.has(idOf(row))" class="animate-spin" />
                  <DownloadIcon v-else />
                  <span class="sr-only">Download file {{ idOf(row) }}</span>
                </Button>
                <Button v-if="actions.edit" variant="ghost" size="icon-xs" title="Edit" @click="openEdit(row)">
                  <PencilIcon />
                  <span class="sr-only">Edit record {{ idOf(row) }}</span>
                </Button>
                <Button v-if="actions.delete" variant="ghost" size="icon-xs" title="Delete" @click="openDelete([idOf(row)])">
                  <Trash2Icon />
                  <span class="sr-only">Delete record {{ idOf(row) }}</span>
                </Button>
                <span v-if="!anyRowAction" class="sr-only">None</span>
              </div>
            </td>
            <td
              v-for="property in properties"
              :key="property.Name"
              :class="cellClass"
              :title="cellText(property, row[property.Name]) ?? 'null'"
            >
              <!-- A button, so a cut-off value can be opened from the keyboard too. -->
              <button
                v-if="cellText(property, row[property.Name]) !== null"
                type="button"
                class="block max-w-64 rounded-sm text-left focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring"
                :class="[
                  expanded.has(`${idOf(row)}:${property.Name}`) ? 'whitespace-pre-wrap wrap-anywhere' : 'truncate',
                  (property.Type === 'Date' || property.Type === 'Number') && 'tabular-nums',
                ]"
                :aria-expanded="expanded.has(`${idOf(row)}:${property.Name}`)"
                @click="toggleCell(row, property.Name)"
              >{{ cellText(property, row[property.Name]) }}</button>
              <span v-else class="text-muted-foreground italic">null</span>
            </td>
          </tr>
        </template>
      </tbody>
    </table>
  </div>

  <RecordPaging v-if="records.data.value && total > 0" :total="total" :page-size="pageSize" />

  <RecordFormSheet
    v-model:open="formOpen"
    :application="application"
    :entity="ordered"
    :record="editing"
    :foreign="foreign"
    @saved="onSaved"
  />

  <FileUploadDialog v-if="files" v-model:open="uploadOpen" :application="application" @uploaded="onUploaded" />

  <RecordHistorySheet
    v-if="actions.history"
    v-model:open="historyOpen"
    :application="application"
    :entity="ordered"
    :record-id="historyId"
  />

  <ConfirmDialog
    v-model:open="deleteOpen"
    :title="deleting.length === 1 ? 'Delete record' : 'Delete records'"
    :description="deleteText"
    confirm-label="Delete"
    destructive
    opener-removed
    :action="submitDelete"
    :error="remove.error.value?.message"
  />

  <ConfirmDialog
    v-if="entity.RequireChangeTracking"
    v-model:open="clearHistoryOpen"
    title="Clear history"
    :description="`Clear the history of every record of ${entityName}? This cannot be undone.`"
    confirm-label="Clear history"
    destructive
    :action="submitClearHistory"
    :error="clearHistory.error.value?.message"
  />
</template>
