<script setup lang="ts">
import { computed, ref } from 'vue'
import { Button } from '@/components/ui/button'
import { Dialog, DialogContent, DialogDescription, DialogHeader, DialogTitle } from '@/components/ui/dialog'
import type { Schemas } from '@/lib/api'
import { describeChange, jsonLinkText, noDetailText, valueHeaders } from '@/lib/auditDiff'

// The detail of one audit log entry: a table of the changed properties. AuditLogTable shows it
// under an opened row. What each cell holds is decided in lib/auditDiff.ts; this file only draws it.
const props = defineProps<{ entry: Schemas['AuditLogEntryResponse'] }>()

const headers = computed(() => valueHeaders(props.entry.Action))
const rows = computed(() =>
  props.entry.Changes.map((change) => ({ property: change.Property, view: describeChange(props.entry.Action, change) })),
)

// The JSON viewer. `viewing` keeps its value while the dialog fades out.
const jsonOpen = ref(false)
const viewing = ref({ property: '', json: '' })

function openJson(property: string, json: string): void {
  viewing.value = { property, json }
  jsonOpen.value = true
}

const tagClass = 'inline-flex rounded-md px-1.5 py-0.5 text-xs font-medium'
const addedClass = 'bg-success/15 text-success'
const removedClass = 'bg-destructive/15 text-destructive'
const updatedClass = 'bg-warning/15 text-warning'
const cellClass = 'border p-2 align-top'
</script>

<template>
  <div class="bg-muted/30 p-3 whitespace-normal">
    <p v-if="rows.length === 0" class="text-sm text-muted-foreground">{{ noDetailText }}</p>

    <table v-else class="w-full table-fixed border-collapse text-xs">
      <thead>
        <tr class="text-left">
          <th :class="cellClass" class="w-1/4 font-medium">Property</th>
          <th v-for="header in headers" :key="header" :class="cellClass" class="font-medium">{{ header }}</th>
        </tr>
      </thead>
      <tbody>
        <tr v-for="(row, index) in rows" :key="index">
          <td :class="cellClass" class="font-mono break-all">{{ row.property }}</td>

          <!-- Security rules: added, updated and removed, across both value columns. -->
          <td v-if="row.view.kind === 'security'" :class="cellClass" colspan="2">
            <p
              v-if="row.view.added.length + row.view.updated.length + row.view.removed.length === 0"
              class="text-muted-foreground"
            >
              No changes
            </p>

            <div v-for="(item, i) in row.view.added" :key="`a${i}`" class="mb-1.5">
              <span :class="[tagClass, addedClass]">Added</span>
              <strong class="ml-1.5 font-medium">{{ item.label }}</strong>
              <span class="ml-1.5 text-muted-foreground">
                Record: {{ item.record }}, Properties: {{ item.properties }}, Rate limit: {{ item.rateLimit }}
              </span>
            </div>

            <div v-for="(item, i) in row.view.updated" :key="`u${i}`" class="mb-1.5">
              <span :class="[tagClass, updatedClass]">Updated</span>
              <strong class="ml-1.5 font-medium">{{ item.label }}</strong>
              <p v-for="diff in item.fields" :key="diff.field" class="mt-0.5 ml-4 break-words">
                <span class="font-mono">{{ diff.field }}</span
                >:
                <span v-if="diff.oldValue" class="text-destructive">{{ diff.oldValue }}</span>
                <template v-if="diff.oldValue && diff.newValue"> → </template>
                <span v-if="diff.newValue" class="text-success">{{ diff.newValue }}</span>
              </p>
            </div>

            <div v-for="(item, i) in row.view.removed" :key="`r${i}`" class="mb-1.5">
              <span :class="[tagClass, removedClass]">Removed</span>
              <strong class="ml-1.5 font-medium">{{ item.label }}</strong>
              <span class="ml-1.5 text-muted-foreground">
                Record: {{ item.record }}, Properties: {{ item.properties }}, Rate limit: {{ item.rateLimit }}
              </span>
            </div>
          </td>

          <!-- Other lists: what left on the old side, what came on the new side. -->
          <template v-else-if="row.view.kind === 'list'">
            <td :class="cellClass">
              <span v-if="row.view.removed.length === 0" class="text-muted-foreground">—</span>
              <span v-for="(label, i) in row.view.removed" :key="i" :class="[tagClass, removedClass]" class="mr-1 mb-1 break-all">
                - {{ label }}
              </span>
            </td>
            <td :class="cellClass">
              <span v-if="row.view.added.length === 0" class="text-muted-foreground">—</span>
              <span v-for="(label, i) in row.view.added" :key="i" :class="[tagClass, addedClass]" class="mr-1 mb-1 break-all">
                + {{ label }}
              </span>
            </td>
          </template>

          <td v-else-if="row.view.kind === 'json'" :class="cellClass">
            <Button
              v-if="row.view.count > 0"
              variant="link"
              size="xs"
              class="h-auto p-0 text-link"
              @click="openJson(row.property, row.view.json)"
            >
              {{ jsonLinkText(row.view.count) }}
            </Button>
            <span v-else class="text-muted-foreground">(empty)</span>
          </td>

          <template v-else>
            <td
              v-for="(cell, i) in row.view.cells"
              :key="i"
              :class="[cellClass, cell.old ? 'text-muted-foreground' : '']"
              class="break-all"
            >
              {{ cell.text }}
            </td>
          </template>
        </tr>
      </tbody>
    </table>

    <Dialog v-model:open="jsonOpen">
      <DialogContent class="max-h-[calc(100dvh-2rem)] grid-rows-[auto_1fr] sm:max-w-2xl">
        <DialogHeader class="pr-8">
          <DialogTitle>{{ viewing.property }}</DialogTitle>
          <DialogDescription class="sr-only">The stored value as JSON.</DialogDescription>
        </DialogHeader>
        <pre class="min-h-0 overflow-auto rounded-md bg-muted/50 p-3 font-mono text-xs">{{ viewing.json }}</pre>
      </DialogContent>
    </Dialog>
  </div>
</template>
