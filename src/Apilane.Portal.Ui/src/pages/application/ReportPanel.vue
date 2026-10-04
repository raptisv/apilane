<script setup lang="ts">
import { EllipsisVerticalIcon, HistoryIcon, LinkIcon, ListOrderedIcon, PencilIcon, RefreshCwIcon, Trash2Icon } from '@lucide/vue'
import { computed } from 'vue'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuSeparator,
  DropdownMenuTrigger,
} from '@/components/ui/dropdown-menu'
import { Skeleton } from '@/components/ui/skeleton'
import { useApplication } from '@/composables/useApplication'
import { useAsync } from '@/composables/useAsync'
import { apiServer } from '@/lib/apiServer'
import { combineSeries, hasNoRows, panelScope, seriesQuery } from '@/lib/reportData'
import type { AggregateRow, Report, SeriesRows } from '@/lib/reportData'
import { reportTypeLabel } from '@/lib/reports'
import ReportChart from './ReportChart.vue'
import ReportTable from './ReportTable.vue'

// One report on the dashboard: its title, what it covers (time range or Top N), Refresh, a menu
// (Edit, View API endpoint, Delete) and the table or chart. Each series is one call to the API
// server (Stats/Aggregate), made by the browser. A series that cannot run or whose call fails says
// so inside the panel; the other series and the other panels are not affected.
const props = defineProps<{
  report: Report
  /** The title is the handle the panel is dragged by (the desktop grid). */
  draggable?: boolean
}>()

defineEmits<{ endpoints: []; delete: [] }>()

const { application } = useApplication()

const scope = computed(() => panelScope(props.report))

/** What one series came back with: its rows, or why there are none. */
interface SeriesState {
  rows?: SeriesRows
  failure?: string
}

async function loadSeries(): Promise<SeriesState[]> {
  const report = props.report
  const server = apiServer(application.value.Server.ServerUrl, application.value.Token)
  const labels = scope.value.labels
  // One moment for all series, taken at every load: Refresh moves the time window to now.
  const now = new Date()

  return Promise.all(
    report.Series.map(async (series, index): Promise<SeriesState> => {
      const label = labels[index] ?? series.Label

      if (series.Error) {
        return { failure: `${label}: ${series.Error}` }
      }

      try {
        const rows = await server.get<AggregateRow[]>('/api/Stats/Aggregate', seriesQuery(report, series, now))

        return { rows: { label, series, rows: Array.isArray(rows) ? rows : [] } }
      } catch (error) {
        return { failure: `${label}: ${error instanceof Error ? error.message : String(error)}` }
      }
    }),
  )
}

// Only what the calls depend on: a reloaded list hands every panel a new object, and a panel
// whose report did not change keeps what it shows.
const definition = computed(() => JSON.stringify([props.report.MaxRecords, props.report.TimeRange, props.report.Series]))

const { data: states, loading, refreshing, reload } = useAsync(loadSeries, { watch: definition })

const failures = computed(() => (states.value ?? []).flatMap((state) => (state.failure ? [state.failure] : [])))
const loaded = computed(() => (states.value ?? []).flatMap((state) => (state.rows ? [state.rows] : [])))
const combined = computed(() => combineSeries(loaded.value))

const chartLabel = computed(
  () => `${props.report.Title}: ${reportTypeLabel(props.report.Type)} of ${loaded.value.map((series) => series.label).join(', ')}`,
)
</script>

<template>
  <section class="flex h-full min-h-0 flex-col overflow-hidden rounded-lg border bg-card" :aria-label="report.Title">
    <header class="flex items-center gap-2 border-b py-1.5 pr-1.5 pl-3">
      <h2
        class="min-w-0 flex-1 truncate py-1 text-sm font-medium"
        :class="draggable ? 'report-drag-handle cursor-move' : ''"
        :title="draggable ? `${report.Title} (drag to move)` : report.Title"
      >
        {{ report.Title }}
      </h2>

      <Badge v-if="scope.windowLabel" variant="outline" class="hidden text-muted-foreground sm:inline-flex" title="Time window applied to date series">
        <HistoryIcon aria-hidden="true" />
        {{ scope.windowLabel }}
      </Badge>
      <Badge v-if="scope.topNLabel" variant="outline" class="hidden text-muted-foreground sm:inline-flex" title="Max points per series">
        <ListOrderedIcon aria-hidden="true" />
        {{ scope.topNLabel }}
      </Badge>

      <div class="flex shrink-0 items-center">
        <Button variant="ghost" size="icon-sm" title="Refresh" :disabled="loading || refreshing" @click="reload">
          <RefreshCwIcon :class="loading || refreshing ? 'animate-spin' : ''" />
          <span class="sr-only">Refresh {{ report.Title }}</span>
        </Button>
        <DropdownMenu>
          <DropdownMenuTrigger as-child>
            <Button variant="ghost" size="icon-sm">
              <EllipsisVerticalIcon />
              <span class="sr-only">Actions for {{ report.Title }}</span>
            </Button>
          </DropdownMenuTrigger>
          <DropdownMenuContent align="end" class="w-48">
            <DropdownMenuItem as-child>
              <RouterLink :to="{ name: 'app-report-edit', params: { appToken: application.Token, reportId: report.ID } }">
                <PencilIcon />
                Edit
              </RouterLink>
            </DropdownMenuItem>
            <DropdownMenuItem @select="$emit('endpoints')">
              <LinkIcon />
              View API endpoint
            </DropdownMenuItem>
            <DropdownMenuSeparator />
            <DropdownMenuItem variant="destructive" @select="$emit('delete')">
              <Trash2Icon />
              Delete
            </DropdownMenuItem>
          </DropdownMenuContent>
        </DropdownMenu>
      </div>
    </header>

    <!-- On a phone the badges do not fit next to the title: they go on a line of their own. -->
    <p v-if="scope.windowLabel || scope.topNLabel" class="border-b px-3 py-1 text-xs text-muted-foreground sm:hidden">
      {{ [scope.windowLabel, scope.topNLabel].filter(Boolean).join(' · ') }}
    </p>

    <div class="flex min-h-0 flex-1 flex-col">
      <div v-if="loading" role="status" class="h-full p-3">
        <span class="sr-only">Loading {{ report.Title }}</span>
        <Skeleton class="h-full w-full" />
      </div>

      <p v-else-if="report.Series.length === 0" class="m-auto p-3 text-sm text-muted-foreground">No series</p>

      <template v-else>
        <ul v-if="failures.length > 0" class="max-h-24 shrink-0 space-y-0.5 overflow-y-auto border-b px-3 py-2 text-xs text-destructive">
          <li v-for="(failure, index) in failures" :key="index" class="wrap-anywhere">{{ failure }}</li>
        </ul>

        <div v-if="loaded.length > 0" class="min-h-0 flex-1">
          <p v-if="hasNoRows(loaded)" class="flex h-full items-center justify-center p-3 text-sm text-muted-foreground">No data</p>
          <ReportTable v-else-if="report.Type === 'Grid'" :data="combined" />
          <ReportChart v-else :type="report.Type" :data="combined" :label="chartLabel" />
        </div>
      </template>
    </div>
  </section>
</template>
