<script setup lang="ts" generic="Item, Changed">
import { CircleMinusIcon, CirclePlusIcon, PencilIcon } from '@lucide/vue'
import { computed, useId } from 'vue'
import DiffCount from './DiffCount.vue'

// One section of a comparison (Entities, Security, ...): the title with its counts, then what was
// added, what was removed and what changed, each as a list. Nothing is drawn when all three lists
// are empty, so a screen can list every section and only the ones that differ show.
//
//   <DiffSection title="Custom endpoints" :added="view.added" :removed="view.removed" :changed="view.changed">
//     <template #item="{ item }">{{ item.Name }}</template>          (an added or a removed one)
//     <template #changed="{ item }"><FieldChange ... /></template>   (one that differs)
//   </DiffSection>
const props = defineProps<{
  title: string
  added: readonly Item[]
  removed: readonly Item[]
  /** Leave it out for things that are only added or removed (constraints). */
  changed?: readonly Changed[]
}>()

defineSlots<{
  item(props: { item: Item }): unknown
  changed(props: { item: Changed }): unknown
}>()

const titleId = useId()

const changedItems = computed(() => props.changed ?? [])
const total = computed(() => props.added.length + props.removed.length + changedItems.value.length)

const listClass = 'mt-1.5 grid gap-3 border-l-2 pl-3'
const groupTitleClass = 'flex items-center gap-1.5 text-xs font-semibold'
</script>

<template>
  <section v-if="total > 0" :aria-labelledby="titleId">
    <div class="mb-3 flex flex-wrap items-center gap-x-2 gap-y-1 border-b pb-2">
      <h3 :id="titleId" class="mr-1 text-sm font-semibold">{{ title }}</h3>
      <DiffCount v-if="added.length > 0" tone="added" :count="added.length" label="added" />
      <DiffCount v-if="removed.length > 0" tone="removed" :count="removed.length" label="removed" />
      <DiffCount v-if="changedItems.length > 0" tone="changed" :count="changedItems.length" label="changed" />
    </div>

    <div class="grid gap-4">
      <div v-if="added.length > 0">
        <h4 :class="groupTitleClass" class="text-success"><CirclePlusIcon class="size-3.5" aria-hidden="true" />Added</h4>
        <ul :class="listClass" class="border-success/60">
          <li v-for="(item, index) in added" :key="index" class="min-w-0"><slot name="item" :item="item" /></li>
        </ul>
      </div>

      <div v-if="removed.length > 0">
        <h4 :class="groupTitleClass" class="text-destructive">
          <CircleMinusIcon class="size-3.5" aria-hidden="true" />Removed
        </h4>
        <ul :class="listClass" class="border-destructive/60">
          <li v-for="(item, index) in removed" :key="index" class="min-w-0"><slot name="item" :item="item" /></li>
        </ul>
      </div>

      <div v-if="changedItems.length > 0">
        <h4 :class="groupTitleClass" class="text-warning"><PencilIcon class="size-3.5" aria-hidden="true" />Changed</h4>
        <ul :class="listClass" class="border-warning/60">
          <li v-for="(item, index) in changedItems" :key="index" class="min-w-0"><slot name="changed" :item="item" /></li>
        </ul>
      </div>
    </div>
  </section>
</template>
