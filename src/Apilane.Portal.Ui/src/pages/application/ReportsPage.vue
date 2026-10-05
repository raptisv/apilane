<script setup lang="ts">
import { ChartLineIcon, CircleAlertIcon, PlusIcon, RefreshCwIcon } from '@lucide/vue'
import { useMediaQuery } from '@vueuse/core'
import { computed, onBeforeUnmount, ref, shallowRef, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import ConfirmDialog from '@/components/ConfirmDialog.vue'
import ErrorState from '@/components/ErrorState.vue'
import LoadingState from '@/components/LoadingState.vue'
import PageHeader from '@/components/PageHeader.vue'
import StateMessage from '@/components/StateMessage.vue'
import ReportsHelp from '@/components/help/ReportsHelp.vue'
import { Alert, AlertDescription, AlertTitle } from '@/components/ui/alert'
import { Button } from '@/components/ui/button'
import { useApplication } from '@/composables/useApplication'
import { useAsync } from '@/composables/useAsync'
import { useMutation } from '@/composables/useMutation'
import { api, ApiError, unwrap } from '@/lib/api'
import type { Schemas } from '@/lib/api'
import type { Report } from '@/lib/reportData'
import * as toast from '@/lib/toast'
import ReportEditorSheet from './ReportEditorSheet.vue'
import ReportEndpointDialog from './ReportEndpointDialog.vue'
import ReportPanel from './ReportPanel.vue'
import type ReportsGridComponent from './ReportsGrid.vue'

type LayoutItem = Schemas['ReportLayoutItem']

// The reports dashboard of one application (/apps/:appToken/reports): its reports as panels. On
// a wide screen they sit on a 12-column grid, are dragged by their title and resized by their
// edges, and the layout is saved a moment after the last change. On a phone they are one column
// in the same order, and nothing moves. The editor is a side sheet over the dashboard, at
// /apps/:appToken/reports/new and /apps/:appToken/reports/<ID>/edit: the three routes share this page.
const route = useRoute()
const router = useRouter()
const { application } = useApplication()

// AppLayout gives every application a fresh screen, so the token is fixed for the life of this one.
const appToken = application.value.Token
const dashboard = { name: 'app-reports', params: { appToken } }

const { data, error, loading, reload } = useAsync(() =>
  unwrap(api.GET('/api/v1/applications/{appToken}/reports', { params: { path: { appToken } } })),
)

const reports = computed(() => data.value?.Data ?? [])

// The layout ----------------------------------------------------------------------------------

const wide = useMediaQuery('(min-width: 768px)')

// The grid (gridstack) is downloaded the first time a dashboard with reports is shown on a wide
// screen. If it cannot be (a deploy while the tab was open, a lost connection), the single column
// of the phone takes over, so the reports still show.
const grid = shallowRef<typeof ReportsGridComponent>()
const gridFailed = ref(false)

watch(
  [wide, () => reports.value.length > 0],
  async ([isWide, any]) => {
    if (!isWide || !any || grid.value) {
      return
    }

    try {
      grid.value = (await import('./ReportsGrid.vue')).default
    } catch {
      gridFailed.value = true
    }
  },
  { immediate: true },
)

// Where the panels were dragged to since the list was loaded: the grid starts from these when it
// is built again (a report added or deleted, the window widened), and the single column follows them.
const moved = ref<Record<number, LayoutItem>>({})

const placed = computed(() =>
  reports.value.map((report) => {
    const at = moved.value[report.ID]

    return at ? { ...report, X: at.X, Y: at.Y, Width: at.Width, Height: at.Height } : report
  }),
)

const stacked = computed(() => [...placed.value].sort((a, b) => a.Y - b.Y || a.X - b.X || a.ID - b.ID))

// A grid is built for one set of reports (see ReportsGrid): another set, or 'Reload', builds a new one.
const gridVersion = ref(0)
const gridKey = computed(
  () =>
    `${gridVersion.value}:${reports.value
      .map((report) => report.ID)
      .sort((a, b) => a - b)
      .join(',')}`,
)

// The layout is saved 600 ms after the last change, one save at a time and in order: a queue of
// its own, because useMutation drops a write that starts while another is running.
const layoutError = shallowRef<Error>()
let timer: ReturnType<typeof setTimeout> | undefined
let waiting: LayoutItem[] | undefined
let queue: Promise<void> = Promise.resolve()

function onLayoutChange(items: LayoutItem[]): void {
  moved.value = Object.fromEntries(items.map((item) => [item.ID, item]))
  waiting = items
  clearTimeout(timer)
  timer = setTimeout(() => void saveLayout(), 600)
}

/** Saves the layout that is waiting, now. Resolves when every save so far is done; it never rejects. */
function saveLayout(): Promise<void> {
  clearTimeout(timer)

  queue = queue.then(async () => {
    const items = waiting

    waiting = undefined

    if (!items) {
      return
    }

    try {
      await unwrap(api.PUT('/api/v1/applications/{appToken}/reports/layout', { params: { path: { appToken } }, body: { Items: items } }))
      layoutError.value = undefined
    } catch (e) {
      layoutError.value = e instanceof Error ? e : new Error(String(e))
    }
  })

  return queue
}

// A change made just before leaving is still saved.
onBeforeUnmount(() => void saveLayout())

const layoutErrorText = computed(() => {
  const failure = layoutError.value

  return failure instanceof ApiError && failure.errors.length > 0
    ? failure.errors.map((detail) => detail.Message).join(' ')
    : failure?.message
})

/** Loads the reports again, after the layout that is waiting has been saved. */
async function refresh(): Promise<void> {
  await saveLayout()
  await reload()
  moved.value = {}
}

/** After a layout that could not be saved: back to what is stored. */
async function reloadDashboard(): Promise<void> {
  clearTimeout(timer)
  waiting = undefined
  layoutError.value = undefined
  await reload()
  moved.value = {}
  gridVersion.value++
}

// The editor ----------------------------------------------------------------------------------

const creating = computed(() => route.name === 'app-report-create')
const editingId = computed(() => (route.name === 'app-report-edit' ? Number(route.params.reportId) : undefined))
const editing = computed(() => reports.value.find((report) => report.ID === editingId.value))

const editorOpen = computed(() => data.value !== undefined && (creating.value || editing.value !== undefined))

// The sheet is mounted the first time it is needed. `editorReport` keeps its value after the
// sheet closes, so the title does not change while it slides out.
const editorSeen = ref(false)
const editorReport = shallowRef<Report>()

watch(
  [editorOpen, editing],
  ([isOpen, report]) => {
    if (isOpen) {
      editorSeen.value = true
      editorReport.value = report
    }
  },
  { immediate: true },
)

// An address of a report that is not there (deleted, or of another application) opens the dashboard.
watch(
  [editingId, data],
  ([id, loaded]) => {
    if (id !== undefined && loaded && !editing.value) {
      toast.error(new Error('This report does not exist.'))
      void router.replace(dashboard)
    }
  },
  { immediate: true },
)

function setEditorOpen(value: boolean): void {
  if (!value) {
    void router.push(dashboard)
  }
}

async function onSaved(): Promise<void> {
  await router.push(dashboard)
  await refresh()
}

// Delete and 'View API endpoint' ----------------------------------------------------------------

// `deleting` and `endpointsOf` keep their value after the dialog closes, so the text does not change while it fades out.
const deleteOpen = ref(false)
const deleting = shallowRef<Report>()

const remove = useMutation(async (id: number) => {
  // First the layout that is waiting: saved after the delete, it would name a report that is gone.
  await saveLayout()
  await unwrap(api.DELETE('/api/v1/applications/{appToken}/reports/{reportId}', { params: { path: { appToken, reportId: id } } }))
})

function openDelete(report: Report): void {
  deleting.value = report
  remove.reset()
  deleteOpen.value = true
}

async function submitDelete(): Promise<boolean> {
  if (!deleting.value || !(await remove.run(deleting.value.ID))) {
    return false
  }

  toast.success('Report deleted.')
  void refresh()
  return true
}

const endpointsOpen = ref(false)
const endpointsOf = shallowRef<Report>()

function openEndpoints(report: Report): void {
  endpointsOf.value = report
  endpointsOpen.value = true
}

/** The height of a panel in the single column: its rows on the grid, and at least four so a chart stays readable. */
function stackedHeight(report: Report): string {
  return `${Math.max(report.Height, 4) * 70}px`
}
</script>

<template>
  <PageHeader title="Reports" description="Tables and charts of the application's data. Each series is a live query to the API server.">
    <ReportsHelp />
    <Button as-child>
      <RouterLink :to="{ name: 'app-report-create', params: { appToken } }">
        <PlusIcon />
        New report
      </RouterLink>
    </Button>
  </PageHeader>

  <LoadingState v-if="loading" label="Loading reports" />

  <ErrorState v-else-if="error" :error="error" @retry="reload" />

  <StateMessage
    v-else-if="reports.length === 0"
    :icon="ChartLineIcon"
    title="No reports yet"
    description="A report counts or sums the records of an entity, grouped by a property or by date, and shows the result as a table or a chart."
  >
    <Button as-child>
      <RouterLink :to="{ name: 'app-report-create', params: { appToken } }">
        <PlusIcon />
        New report
      </RouterLink>
    </Button>
  </StateMessage>

  <template v-else>
    <Alert v-if="layoutError" variant="destructive" class="mb-4">
      <CircleAlertIcon />
      <AlertTitle>The layout was not saved</AlertTitle>
      <AlertDescription>
        <p class="wrap-anywhere">{{ layoutErrorText }}</p>
        <p>The dashboard may have changed in another tab. Reload it and move the panel again.</p>
        <Button variant="outline" size="sm" class="mt-3" @click="reloadDashboard">
          <RefreshCwIcon />
          Reload the dashboard
        </Button>
      </AlertDescription>
    </Alert>

    <!-- Wide screen: the grid. -mx-1.5 lines the panels up with the page: gridstack puts a margin around each. -->
    <div v-if="wide && grid" class="-mx-1.5">
      <component :is="grid" :key="gridKey" :reports="placed" @change="onLayoutChange">
        <template #default="{ report }">
          <ReportPanel :report="report" draggable @endpoints="openEndpoints(report)" @delete="openDelete(report)" />
        </template>
      </component>
    </div>

    <LoadingState v-else-if="wide && !gridFailed" label="Loading the dashboard" />

    <!-- Phone: one column, in the order of the grid (top to bottom, then left to right). -->
    <ul v-else class="grid grid-cols-1 gap-3" aria-label="Reports">
      <li v-for="report in stacked" :key="report.ID" :style="{ height: stackedHeight(report) }">
        <ReportPanel :report="report" @endpoints="openEndpoints(report)" @delete="openDelete(report)" />
      </li>
    </ul>
  </template>

  <ReportEditorSheet
    v-if="editorSeen"
    :open="editorOpen"
    :app-token="appToken"
    :report="editorReport"
    @update:open="setEditorOpen"
    @saved="onSaved"
  />

  <ReportEndpointDialog v-if="endpointsOf" v-model:open="endpointsOpen" :report="endpointsOf" />

  <ConfirmDialog
    v-if="deleting"
    v-model:open="deleteOpen"
    :title="`Delete report '${deleting.Title}'?`"
    description="The data it shows is not touched. This cannot be undone."
    confirm-label="Delete"
    destructive
    :action="submitDelete"
    :error="remove.error.value?.message"
  />
</template>
