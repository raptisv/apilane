<script setup lang="ts">
import { computed } from 'vue'
import { collapseUnchanged, diffLines, hasChanges } from '@/lib/lineDiff'

// What changed between two texts, line by line, in a fixed-width font: removed lines in red with a
// minus, added lines in green with a plus, and the lines around them for context. A long run of
// unchanged lines is folded into one line that says how many it hides. The sign is also a word for
// a screen reader.
//
//   <LineDiff :before="saved.Query" :after="form.Query" />
const props = defineProps<{
  before: string
  after: string
}>()

const all = computed(() => diffLines(props.before, props.after))
const changed = computed(() => hasChanges(all.value))
const lines = computed(() => collapseUnchanged(all.value))

const rowClass = {
  same: '',
  added: 'bg-success/15',
  removed: 'bg-destructive/15',
  gap: 'bg-muted/50 text-muted-foreground',
} as const

const signs = { same: ' ', added: '+', removed: '-', gap: '' } as const
const words = { same: '', added: 'Added: ', removed: 'Removed: ', gap: '' } as const
</script>

<template>
  <p v-if="!changed" class="text-sm text-muted-foreground">The SQL is the same.</p>

  <div v-else class="overflow-x-auto rounded-md border bg-muted/20" role="group" aria-label="Changes to the SQL">
    <table class="w-full border-collapse font-mono text-xs">
      <tbody>
        <tr v-for="(line, index) in lines" :key="index" :class="rowClass[line.kind]">
          <template v-if="line.kind === 'gap'">
            <td colspan="4" class="px-3 py-1 text-center italic">{{ line.skipped }} unchanged {{ line.skipped === 1 ? 'line' : 'lines' }}</td>
          </template>
          <template v-else>
            <td class="w-10 px-2 text-right align-top text-muted-foreground select-none" aria-hidden="true">{{ line.oldNumber }}</td>
            <td class="w-10 px-2 text-right align-top text-muted-foreground select-none" aria-hidden="true">{{ line.newNumber }}</td>
            <td class="w-5 px-1 text-center align-top select-none" aria-hidden="true">{{ signs[line.kind] }}</td>
            <td class="px-2 pr-3 break-words whitespace-pre-wrap"><span class="sr-only">{{ words[line.kind] }}</span>{{ line.text }}</td>
          </template>
        </tr>
      </tbody>
    </table>
  </div>
</template>
