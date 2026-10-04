<script setup lang="ts">
import 'gridstack/dist/gridstack.min.css'
import { GridStack } from 'gridstack'
import { onBeforeUnmount, onMounted, useTemplateRef } from 'vue'
import type { Schemas } from '@/lib/api'
import type { Report } from '@/lib/reportData'

// The dashboard on a wide screen: a 12-column grid (gridstack) whose
// panels are dragged by their title ('.report-drag-handle') and resized by their edges. What a
// panel shows comes from the default slot. ReportsPage loads this component with a dynamic import,
// so gridstack is downloaded only when a dashboard is shown on a wide screen.
//
// Vue draws the panels and gridstack places them. The places are handed to gridstack once, when
// the grid is mounted; afterwards gridstack owns them and reports every change through `change`.
// So the set of reports must not change under a mounted grid: ReportsPage gives the grid a `key`
// made of the report IDs, which mounts a new one when a report is added or deleted.
type LayoutItem = Schemas['ReportLayoutItem']

const props = defineProps<{ reports: Report[] }>()

const emit = defineEmits<{
  /** The place and size of every panel, after one was moved or resized. */
  change: [items: LayoutItem[]]
}>()

defineSlots<{ default(props: { report: Report }): unknown }>()

const gridEl = useTemplateRef<HTMLElement>('gridEl')
let grid: GridStack | undefined

function layout(): LayoutItem[] {
  return (grid?.engine.nodes ?? []).flatMap((node) => {
    const id = Number(node.id)

    return Number.isInteger(id) && id > 0 ? [{ ID: id, X: node.x ?? 0, Y: node.y ?? 0, Width: node.w ?? 1, Height: node.h ?? 1 }] : []
  })
}

onMounted(() => {
  const el = gridEl.value

  if (!el) {
    return
  }

  // gridstack reads the place of a panel from these attributes. They are set here and not in the
  // template, so that a later render never writes an old place over the one gridstack keeps.
  for (const item of el.querySelectorAll<HTMLElement>(':scope > .grid-stack-item')) {
    const report = props.reports.find((candidate) => String(candidate.ID) === item.dataset.reportId)

    if (report) {
      item.setAttribute('gs-id', String(report.ID))
      item.setAttribute('gs-x', String(report.X))
      item.setAttribute('gs-y', String(report.Y))
      item.setAttribute('gs-w', String(report.Width))
      item.setAttribute('gs-h', String(report.Height))
    }
  }

  // Stored layouts are in these units (12 columns, rows of 70px): changing them reshapes every
  // saved dashboard. 'top' is what gridstack 14 calls float: false: panels move up into gaps.
  grid =
    GridStack.init(
      { column: 12, cellHeight: 70, margin: 6, handle: '.report-drag-handle', mode: 'top', resizable: { handles: 'e, se, s, sw, w' } },
      el,
    ) ?? undefined

  grid?.on('change', () => emit('change', layout()))
})

// false: gridstack lets go of the elements and Vue removes them.
onBeforeUnmount(() => grid?.destroy(false))
</script>

<template>
  <div ref="gridEl" class="grid-stack">
    <div v-for="report in reports" :key="report.ID" class="grid-stack-item" :data-report-id="report.ID">
      <!-- gridstack clips the content of an item; the panel clips its own body, and its focus ring must stay visible. -->
      <div class="grid-stack-item-content overflow-visible!">
        <slot :report="report" />
      </div>
    </div>
  </div>
</template>
