<script setup lang="ts">
import { CircleAlertIcon } from '@lucide/vue'
import { onBeforeUnmount, onMounted, shallowRef, useTemplateRef, watch } from 'vue'
import { Skeleton } from '@/components/ui/skeleton'
import { chartConfig } from '@/lib/reportData'
import type { CombinedReport } from '@/lib/reportData'
import type { ReportChartHandle } from '@/lib/reportChart'
import ReportTable from './ReportTable.vue'

// The body of a report that is a chart (Pie, Line, Bar, StackedBar, Radar). Chart.js is downloaded
// the first time a chart is shown; a placeholder shows until then. The chart follows the size of
// its panel.
const props = defineProps<{
  /** The Type of the report. */
  type: string
  data: CombinedReport
  /** What the chart shows, for screen readers: a canvas has no text of its own. */
  label: string
}>()

const canvas = useTemplateRef<HTMLCanvasElement>('canvas')
const ready = shallowRef(false)
// If Chart.js cannot be downloaded (a deploy while the tab was open, a lost connection) the
// numbers are shown as a table instead, so the panel still works.
const failed = shallowRef(false)

let chart: ReportChartHandle | undefined
let unmounted = false

async function draw(): Promise<void> {
  try {
    const { createReportChart, reportPalette } = await import('@/lib/reportChart')

    if (unmounted || !canvas.value) {
      return
    }

    // A chart is drawn afresh for new data.
    chart?.destroy()
    chart = createReportChart(canvas.value, chartConfig(props.type, props.data, reportPalette()))
    ready.value = true
  } catch {
    failed.value = true
  }
}

onMounted(draw)
watch(() => [props.type, props.data], draw)

onBeforeUnmount(() => {
  unmounted = true
  chart?.destroy()
})
</script>

<template>
  <div v-if="failed" class="flex h-full min-h-0 flex-col">
    <p class="flex items-center gap-1.5 px-3 py-1.5 text-xs text-muted-foreground">
      <CircleAlertIcon class="size-3.5 shrink-0" aria-hidden="true" />
      The chart could not be loaded. Reload the page to get it back.
    </p>
    <div class="min-h-0 flex-1">
      <ReportTable :data="data" />
    </div>
  </div>

  <div v-else class="relative h-full min-h-0 p-1.5">
    <!-- Chart.js sizes the canvas to its parent, so the parent holds nothing but the canvas. -->
    <div class="relative h-full w-full">
      <canvas ref="canvas" role="img" :aria-label="label" />
    </div>
    <div v-if="!ready" role="status" class="absolute inset-0 p-1.5">
      <span class="sr-only">Loading the chart</span>
      <Skeleton class="h-full w-full" />
    </div>
  </div>
</template>
