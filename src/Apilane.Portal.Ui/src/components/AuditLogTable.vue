<script setup lang="ts">
import { ChevronDownIcon, CirclePlusIcon, PencilIcon, Trash2Icon } from '@lucide/vue'
import { ref } from 'vue'
import type { Component } from 'vue'
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@/components/ui/table'
import type { Schemas } from '@/lib/api'
import { formatUtc } from '@/lib/format'
import AuditLogDetail from './AuditLogDetail.vue'

// One page of audit log entries. A row opens to show what changed (AuditLogDetail). Used by the
// instance audit log and the application audit log: the screen loads the entries and adds
// AppPagination under the table.
defineProps<{ entries: Schemas['AuditLogEntryResponse'][] }>()

// The IDs of the opened rows.
const open = ref(new Set<number>())

function toggle(id: number): void {
  if (!open.value.delete(id)) {
    open.value.add(id)
  }
}

// Created is green, Deleted is red, anything else is yellow with a pencil.
function actionStyle(action: string): { icon: Component; class: string } {
  if (action === 'Created') {
    return { icon: CirclePlusIcon, class: 'text-success' }
  }

  return action === 'Deleted' ? { icon: Trash2Icon, class: 'text-destructive' } : { icon: PencilIcon, class: 'text-warning' }
}
</script>

<template>
  <div class="overflow-hidden rounded-lg border bg-card">
    <Table>
      <TableHeader>
        <TableRow>
          <TableHead class="hidden pl-4 md:table-cell">Timestamp (UTC)</TableHead>
          <TableHead class="hidden md:table-cell">User</TableHead>
          <TableHead class="pl-4 md:pl-2">Action</TableHead>
          <TableHead class="hidden md:table-cell">Type</TableHead>
          <TableHead class="hidden md:table-cell">Target</TableHead>
          <TableHead class="pr-4"><span class="sr-only">Details</span></TableHead>
        </TableRow>
      </TableHeader>
      <TableBody>
        <template v-for="entry in entries" :key="entry.ID">
          <TableRow class="cursor-pointer" @click="toggle(entry.ID)">
            <TableCell class="hidden pl-4 font-mono text-xs md:table-cell">{{ formatUtc(entry.Timestamp) }}</TableCell>
            <TableCell class="hidden text-xs break-all whitespace-normal md:table-cell">{{ entry.UserEmail }}</TableCell>
            <TableCell class="pl-4 whitespace-normal md:pl-2 md:whitespace-nowrap">
              <span class="inline-flex items-center gap-1.5" :class="actionStyle(entry.Action).class">
                <component :is="actionStyle(entry.Action).icon" class="size-3.5" aria-hidden="true" />
                {{ entry.Action }}
              </span>
              <!-- On a phone the other columns move under the action, so the table fits the screen. -->
              <div class="mt-1 md:hidden">
                <p class="wrap-anywhere">
                  {{ entry.EntityType }} <strong class="font-medium">{{ entry.EntityIdentifier }}</strong>
                </p>
                <p class="mt-0.5 text-xs wrap-anywhere text-muted-foreground">
                  <span class="font-mono">{{ formatUtc(entry.Timestamp) }}</span> UTC · {{ entry.UserEmail }}
                </p>
              </div>
            </TableCell>
            <TableCell class="hidden whitespace-normal md:table-cell">{{ entry.EntityType }}</TableCell>
            <TableCell class="hidden font-medium wrap-anywhere whitespace-normal md:table-cell">
              {{ entry.EntityIdentifier }}
            </TableCell>
            <TableCell class="w-px pr-4 align-top md:align-middle">
              <!-- The row is clickable for the mouse; this button is the same action for the keyboard. -->
              <button
                type="button"
                class="flex size-7 items-center justify-center rounded-md hover:bg-muted focus-visible:outline-2 focus-visible:outline-ring"
                :aria-expanded="open.has(entry.ID)"
                @click.stop="toggle(entry.ID)"
              >
                <ChevronDownIcon class="size-4 transition-transform" :class="open.has(entry.ID) ? 'rotate-180' : ''" />
                <span class="sr-only">
                  Details of {{ entry.Action }} {{ entry.EntityType }} {{ entry.EntityIdentifier }}
                </span>
              </button>
            </TableCell>
          </TableRow>
          <TableRow v-if="open.has(entry.ID)" class="hover:bg-transparent">
            <TableCell colspan="6" class="p-0">
              <AuditLogDetail :entry="entry" />
            </TableCell>
          </TableRow>
        </template>
      </TableBody>
    </Table>
  </div>
</template>
