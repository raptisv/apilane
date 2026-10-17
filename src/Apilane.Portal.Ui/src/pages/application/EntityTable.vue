<script setup lang="ts">
import { Table, TableBody, TableHead, TableHeader, TableRow } from '@/components/ui/table'
import { useApplication } from '@/composables/useApplication'
import type { Entity } from '@/lib/entities'
import EntityRow from './EntityRow.vue'

// One of the two tables of the entities page (custom entities, system entities).
defineProps<{
  entities: Entity[]
  /** The names a search keeps. The other rows are only hidden, so their counts are not loaded again. */
  matching: Set<string>
  /** By entity name: the entity whose foreign key points to it, if any. */
  referencedBy: ReadonlyMap<string, string | undefined>
}>()
defineEmits<{ edit: [entity: Entity]; rename: [entity: Entity]; delete: [entity: Entity] }>()

const { application } = useApplication()
</script>

<template>
  <div class="overflow-hidden rounded-lg border bg-card">
    <Table class="table-fixed">
      <TableHeader>
        <TableRow>
          <TableHead class="w-12 pl-4"><span class="sr-only">Access</span></TableHead>
          <TableHead class="pl-2">Entity</TableHead>
          <TableHead class="hidden w-40 text-right md:table-cell">Records</TableHead>
          <TableHead class="hidden w-36 md:table-cell">Change tracking</TableHead>
          <TableHead v-if="application.DifferentiationEntity" class="hidden w-32 md:table-cell" title="Differentiation property">
            Differentiation
          </TableHead>
          <TableHead class="w-14 pr-4"><span class="sr-only">Actions</span></TableHead>
        </TableRow>
      </TableHeader>
      <TableBody>
        <EntityRow
          v-for="entity in entities"
          v-show="matching.has(entity.Name)"
          :key="entity.Name"
          :entity="entity"
          :referenced-by="referencedBy.get(entity.Name)"
          @edit="$emit('edit', entity)"
          @rename="$emit('rename', entity)"
          @delete="$emit('delete', entity)"
        />
      </TableBody>
    </Table>
  </div>
</template>
