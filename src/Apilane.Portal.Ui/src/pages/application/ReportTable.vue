<script setup lang="ts">
import { gridText } from '@/lib/reportData'
import type { CombinedReport } from '@/lib/reportData'

// The body of a Grid report: one row per group, one column per series. It scrolls inside its
// panel with the header row staying in view, which is why it is a plain table and not the shared
// Table component (that one brings its own scrolling box).
// Labels and values are data of the application: they are written as text, never as HTML.
defineProps<{ data: CombinedReport }>()
</script>

<template>
  <div class="relative h-full overflow-auto">
    <table class="w-full text-sm">
      <thead class="sticky top-0 bg-card text-left text-xs text-muted-foreground">
        <tr>
          <th scope="col" class="border-b px-3 py-2 font-medium">Group</th>
          <th v-for="(series, index) in data.series" :key="index" scope="col" class="border-b px-3 py-2 font-medium">
            {{ series.label }}
          </th>
        </tr>
      </thead>
      <tbody>
        <tr v-for="(label, row) in data.labels" :key="label" class="border-b last:border-b-0 hover:bg-muted/40">
          <th scope="row" class="px-3 py-1.5 text-left font-medium wrap-anywhere">{{ label }}</th>
          <td v-for="(series, index) in data.series" :key="index" class="px-3 py-1.5 wrap-anywhere tabular-nums">
            {{ gridText(series.values[row]) }}
          </td>
        </tr>
      </tbody>
    </table>
  </div>
</template>
