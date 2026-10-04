<script setup lang="ts">
import { computed } from 'vue'
import { Input } from '@/components/ui/input'
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'
import type { Property } from '@/lib/properties'
import { filterOperators } from '@/lib/records'
import type { ColumnFilter } from '@/lib/records'

// The filter box of one grid column, by the property's type: a String has
// 'contains' or 'equal' (a button switches), an encrypted String 'equal' only, a Number and a Date
// 'equal', a Boolean a choice of true / false.
const props = defineProps<{ property: Property }>()

const filter = defineModel<ColumnFilter>({ required: true })

const operators = computed(() => filterOperators(props.property))

function switchOperator(): void {
  filter.value = { ...filter.value, operator: filter.value.operator === 'contains' ? 'equal' : 'contains' }
}

function setValue(value: string | number): void {
  filter.value = { ...filter.value, value }
}

// The Boolean select cannot hold '' (no filter): 'any' stands for it.
const booleanValue = computed({
  get: () => (filter.value.value === '' ? 'any' : String(filter.value.value)),
  set: (value: string) => setValue(value === 'any' ? '' : value),
})

const step = computed(() => {
  const places = props.property.DecimalPlaces ?? 0
  return places > 0 ? `0.${'0'.repeat(places - 1)}1` : '1'
})

const label = computed(() => `Filter ${props.property.Name}`)
</script>

<template>
  <Select v-if="property.Type === 'Boolean'" v-model="booleanValue">
    <SelectTrigger size="sm" class="w-full min-w-24" :aria-label="label">
      <SelectValue />
    </SelectTrigger>
    <SelectContent>
      <SelectItem value="any">Any</SelectItem>
      <SelectItem value="true">true</SelectItem>
      <SelectItem value="false">false</SelectItem>
    </SelectContent>
  </Select>

  <Input
    v-else-if="property.Type === 'Number'"
    :model-value="filter.value"
    type="number"
    :step="step"
    inputmode="decimal"
    class="h-7 min-w-28"
    :aria-label="`${label} (equal to)`"
    @update:model-value="setValue"
  />

  <Input
    v-else-if="property.Type === 'Date'"
    :model-value="filter.value"
    class="h-7 min-w-36 font-mono text-xs"
    :placeholder="property.IsUtc ? 'UTC yyyy-MM-dd HH:mm:ss' : 'yyyy-MM-dd HH:mm:ss'"
    spellcheck="false"
    :aria-label="property.IsUtc ? `${label} (equal to, UTC; the grid shows local time)` : `${label} (equal to, UTC)`"
    @update:model-value="setValue"
  />

  <div v-else class="flex min-w-36 items-center gap-1">
    <button
      v-if="operators.length > 1"
      type="button"
      class="h-7 shrink-0 rounded-md border border-input px-1.5 font-mono text-xs text-muted-foreground transition-colors hover:bg-muted hover:text-foreground focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring"
      :title="filter.operator === 'contains' ? 'Contains the text. Click for equal to.' : 'Equal to the text. Click for contains.'"
      @click="switchOperator"
    >
      <span aria-hidden="true">{{ filter.operator === 'contains' ? '~' : '=' }}</span>
      <span class="sr-only">{{ property.Name }}: {{ filter.operator === 'contains' ? 'contains' : 'equal to' }}. Switch.</span>
    </button>
    <Input
      :model-value="filter.value"
      class="h-7"
      spellcheck="false"
      :title="property.Encrypted ? 'Encrypted: finds the whole value only' : undefined"
      :aria-label="`${label} (${filter.operator === 'contains' ? 'contains' : 'equal to'})`"
      @update:model-value="setValue"
    />
  </div>
</template>
