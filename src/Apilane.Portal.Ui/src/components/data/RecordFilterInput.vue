<script setup lang="ts">
import { computed } from 'vue'
import { Input } from '@/components/ui/input'
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'
import type { Property } from '@/lib/properties'
import { filterOperatorText, filterOperators, nextFilterOperator } from '@/lib/records'
import type { ColumnFilter } from '@/lib/records'

// The filter box of one grid column, by the property's type: a String has 'contains' or 'equal', a
// Number and a Date 'equal', 'at least' or 'at most' (a button switches), an encrypted String
// 'equal' only, a Boolean a choice of true / false.
const props = defineProps<{ property: Property }>()

const filter = defineModel<ColumnFilter>({ required: true })

const operators = computed(() => filterOperators(props.property))
const operator = computed(() => filterOperatorText(props.property, filter.value.operator))
const next = computed(() => filterOperatorText(props.property, nextFilterOperator(props.property, filter.value.operator)))

function switchOperator(): void {
  filter.value = { ...filter.value, operator: nextFilterOperator(props.property, filter.value.operator) }
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

  <div v-else class="flex items-center gap-1" :class="property.Type === 'Date' ? 'min-w-44' : 'min-w-36'">
    <button
      v-if="operators.length > 1"
      type="button"
      class="h-7 shrink-0 rounded-md border border-input px-1.5 font-mono text-xs text-muted-foreground transition-colors hover:bg-muted hover:text-foreground focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring"
      :title="`Filter: ${operator.words}. Click for ${next.words}.`"
      @click="switchOperator"
    >
      <span aria-hidden="true">{{ operator.symbol }}</span>
      <span class="sr-only">{{ property.Name }}: {{ operator.words }}. Switch.</span>
    </button>

    <Input
      v-if="property.Type === 'Number'"
      :model-value="filter.value"
      type="number"
      :step="step"
      inputmode="decimal"
      class="h-7 min-w-24"
      :aria-label="`${label} (${operator.words})`"
      @update:model-value="setValue"
    />

    <!-- The day alone is the whole day; a time narrows it to that minute or second (dateSpan). -->
    <Input
      v-else-if="property.Type === 'Date'"
      :model-value="filter.value"
      class="h-7 min-w-36 font-mono text-xs"
      :placeholder="property.IsUtc ? 'UTC yyyy-MM-dd HH:mm:ss' : 'yyyy-MM-dd HH:mm:ss'"
      spellcheck="false"
      :aria-label="
        property.IsUtc ? `${label} (${operator.words}, UTC; the grid shows local time)` : `${label} (${operator.words}, UTC)`
      "
      @update:model-value="setValue"
    />

    <Input
      v-else
      :model-value="filter.value"
      class="h-7"
      spellcheck="false"
      :title="property.Encrypted ? 'Encrypted: compared with the stored, encrypted text' : undefined"
      :aria-label="`${label} (${operator.words})`"
      @update:model-value="setValue"
    />
  </div>
</template>
