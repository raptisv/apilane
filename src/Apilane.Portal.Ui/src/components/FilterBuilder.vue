<script setup lang="ts">
import { PlusIcon, XIcon } from '@lucide/vue'
import { nextTick, useTemplateRef } from 'vue'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'
import { allFilterOperators, newFilterRow } from '@/lib/filterBuilder'
import type { FilterRow } from '@/lib/filterBuilder'

// Builds a filter of the data API as a list of conditions (property, operator, value) that must
// all hold. Use it wherever a stored filter is edited (the series of a report). It edits rows;
// the text to store comes from filterText, and a stored text becomes rows with parseFilter
// (lib/filterBuilder.ts):
//
//   <FilterBuilder v-model="rows" :properties="entity.Properties.map((property) => property.Name)" />
const props = defineProps<{
  /** The names of the properties a condition can be about. */
  properties: string[]
}>()

const rows = defineModel<FilterRow[]>({ required: true })

const list = useTemplateRef<HTMLElement>('list')

/** A condition about a property the entity no longer has still shows its name, so it can be seen and removed. */
function propertyOptions(row: FilterRow): string[] {
  return row.property === '' || props.properties.includes(row.property) ? props.properties : [row.property, ...props.properties]
}

function update(index: number, change: Partial<FilterRow>): void {
  rows.value = rows.value.map((row, place) => (place === index ? { ...row, ...change } : row))
}

async function add(): Promise<void> {
  rows.value = [...rows.value, newFilterRow(props.properties)]

  // Go on in the new condition.
  await nextTick()
  list.value?.querySelector<HTMLElement>('li:last-child [data-first]')?.focus()
}

async function remove(index: number): Promise<void> {
  rows.value = rows.value.filter((_, place) => place !== index)

  // The button under the pointer is gone: the focus moves to the one now in its place, or to Add.
  await nextTick()

  const buttons = list.value?.querySelectorAll<HTMLElement>('[data-remove]') ?? []
  const next = buttons[Math.min(index, buttons.length - 1)] ?? list.value?.parentElement?.querySelector<HTMLElement>('[data-add]')

  next?.focus()
}
</script>

<template>
  <div class="grid gap-3">
    <p v-if="rows.length === 0" class="text-sm text-muted-foreground">No conditions: every record is included.</p>

    <ul ref="list" class="grid gap-3" aria-label="Conditions">
      <!-- On a phone the remove button sits next to the property; the operator and the value go below. -->
      <li
        v-for="(row, index) in rows"
        :key="index"
        class="grid grid-cols-[minmax(0,1fr)_auto] gap-2 sm:grid-cols-[minmax(0,1fr)_minmax(0,1fr)_minmax(0,1fr)_auto]"
      >
        <Select :model-value="row.property" @update:model-value="(value) => update(index, { property: String(value) })">
          <SelectTrigger class="w-full" data-first :aria-label="`Property of condition ${index + 1}`">
            <SelectValue placeholder="Property" />
          </SelectTrigger>
          <SelectContent>
            <SelectItem v-for="name in propertyOptions(row)" :key="name" :value="name">{{ name }}</SelectItem>
          </SelectContent>
        </Select>

        <Select :model-value="row.operator" @update:model-value="(value) => update(index, { operator: String(value) })">
          <SelectTrigger class="col-span-2 w-full sm:col-span-1" :aria-label="`Operator of condition ${index + 1}`">
            <SelectValue />
          </SelectTrigger>
          <SelectContent>
            <SelectItem v-for="operator in allFilterOperators" :key="operator" :value="operator">{{ operator }}</SelectItem>
          </SelectContent>
        </Select>

        <Input
          :model-value="row.value"
          class="col-span-2 sm:col-span-1"
          placeholder="Value"
          autocomplete="off"
          :aria-label="`Value of condition ${index + 1}`"
          @update:model-value="(value) => update(index, { value: String(value) })"
        />

        <Button
          type="button"
          variant="ghost"
          size="icon"
          class="col-start-2 row-start-1 sm:col-start-auto sm:row-start-auto"
          title="Remove"
          data-remove
          @click="remove(index)"
        >
          <XIcon />
          <span class="sr-only">Remove condition {{ index + 1 }}</span>
        </Button>
      </li>
    </ul>

    <div>
      <Button type="button" variant="outline" size="sm" data-add @click="add">
        <PlusIcon />
        Add condition
      </Button>
    </div>

    <p class="text-xs text-muted-foreground">
      Type <span class="font-mono">null</span> as the value to compare with no value. Write a date as 2026-01-31 or 2026-01-31 14:30.
      On a number, <span class="font-mono">contains</span> with a comma-separated list means 'one of': 1,2,3.
    </p>
  </div>
</template>
