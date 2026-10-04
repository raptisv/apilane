<script setup lang="ts">
import { ref } from 'vue'
import { Input } from '@/components/ui/input'
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'
import { parseTimeRange } from '@/lib/reportData'
import { quickTimeRanges, timeUnits } from '@/lib/reports'

// The time range of a report in the editor: one of the quick ranges, or 'Custom'
// with a number and a unit. The value is the code the API stores ('7d'), '' for none. Inside a
// FormField bind the slot's `field` onto it like onto an Input.
defineOptions({ inheritAttrs: false })

const code = defineModel<string>({ required: true })

// A select item cannot have an empty value: these two stand for 'no time range' and 'Custom'.
const none = 'none'
const customChoice = 'custom'

const stored = parseTimeRange(code.value)

// A stored range that is not one of the quick ones opens as 'Custom'.
const custom = ref(!quickTimeRanges.some((range) => range.code === code.value))
const amount = ref<number | string>(stored?.amount ?? (custom.value ? '' : 1))
const unit = ref<string>(stored?.unit ?? 'd')

function setCustom(): void {
  // An empty number box gives a code the API refuses, so the mistake shows at this field.
  code.value = `${amount.value}${unit.value}`
}

function choose(value: unknown): void {
  custom.value = value === customChoice

  if (custom.value) {
    setCustom()
  } else {
    code.value = value === none ? '' : String(value)
  }
}

function setAmount(value: string | number): void {
  amount.value = value
  setCustom()
}

function setUnit(value: unknown): void {
  unit.value = String(value)
  setCustom()
}
</script>

<template>
  <div class="grid gap-2">
    <Select :model-value="custom ? customChoice : code === '' ? none : code" @update:model-value="choose">
      <SelectTrigger v-bind="$attrs" class="w-full">
        <SelectValue />
      </SelectTrigger>
      <SelectContent>
        <SelectItem v-for="range in quickTimeRanges" :key="range.code" :value="range.code === '' ? none : range.code">
          {{ range.label }}
        </SelectItem>
        <SelectItem :value="customChoice">Custom</SelectItem>
      </SelectContent>
    </Select>

    <div v-if="custom" class="flex items-center gap-2">
      <span class="text-sm text-muted-foreground">Last</span>
      <Input
        :model-value="amount"
        type="number"
        min="1"
        max="1000"
        step="1"
        inputmode="numeric"
        class="w-20"
        aria-label="Number of the custom time range"
        @update:model-value="setAmount"
      />
      <Select :model-value="unit" @update:model-value="setUnit">
        <SelectTrigger class="min-w-0 flex-1" aria-label="Unit of the custom time range">
          <SelectValue />
        </SelectTrigger>
        <SelectContent>
          <SelectItem v-for="item in timeUnits" :key="item.value" :value="item.value">{{ item.label }}</SelectItem>
        </SelectContent>
      </Select>
    </div>
  </div>
</template>
