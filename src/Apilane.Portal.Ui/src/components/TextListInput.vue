<script setup lang="ts">
import { PlusIcon, XIcon } from '@lucide/vue'
import { nextTick, useId, useTemplateRef } from 'vue'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'

// A list of short values, one box each, with Add and Remove (the IP addresses of the security
// settings). Each entry can carry its own error, from the API's 'List[2]' messages. Pasting text
// with commas, spaces or new lines into a box splits it into several entries.
//
//   <TextListInput v-model="form.ClientIPs" item-label="IP address" add-label="Add address" :errors="ipErrors" />
const props = defineProps<{
  /** What one entry is, for the screen-reader names ('IP address 2'). */
  itemLabel: string
  addLabel: string
  placeholder?: string
  /** Place in the list to its message. */
  errors?: Record<number, string>
  disabled?: boolean
}>()

const values = defineModel<string[]>({ required: true })

const id = useId()
const list = useTemplateRef<HTMLElement>('list')
const addButton = useTemplateRef('addButton')

// Falls back to the Add button when the entry is gone (the last one was removed), so the focus
// never drops to the page.
async function focusEntry(index: number): Promise<void> {
  await nextTick()
  const entry = list.value?.querySelector<HTMLInputElement>(`[data-entry="${index}"]`)
  const target: HTMLElement | null | undefined = entry ?? addButton.value?.$el
  target?.focus()
}

function setEntry(index: number, value: string | number): void {
  values.value = values.value.map((entry, i) => (i === index ? String(value) : entry))
}

function add(): void {
  values.value = [...values.value, '']
  void focusEntry(values.value.length - 1)
}

function remove(index: number): void {
  values.value = values.value.filter((_, i) => i !== index)
  // Keep the focus in the list: on the entry now in that place, the one before it, or Add when none is left.
  void focusEntry(Math.min(index, values.value.length - 1))
}

function paste(index: number, event: ClipboardEvent): void {
  const parts = (event.clipboardData?.getData('text') ?? '').split(/[\s,;]+/).filter((part) => part !== '')

  if (parts.length < 2) {
    return
  }

  event.preventDefault()
  values.value = [...values.value.slice(0, index), ...parts, ...values.value.slice(index + 1)]
  void focusEntry(index + parts.length - 1)
}
</script>

<template>
  <div class="grid gap-2">
    <ul v-if="values.length > 0" ref="list" class="grid gap-2">
      <li v-for="(value, index) in values" :key="index" class="grid gap-1">
        <div class="flex gap-1.5">
          <Input
            :model-value="value"
            :data-entry="index"
            :aria-label="`${itemLabel} ${index + 1}`"
            :aria-invalid="props.errors?.[index] ? true : undefined"
            :aria-describedby="props.errors?.[index] ? `${id}-error-${index}` : undefined"
            :placeholder="placeholder"
            :disabled="disabled"
            autocomplete="off"
            spellcheck="false"
            class="font-mono"
            @update:model-value="setEntry(index, $event)"
            @paste="paste(index, $event)"
          />
          <Button type="button" variant="ghost" size="icon" title="Remove" :disabled="disabled" @click="remove(index)">
            <XIcon />
            <span class="sr-only">Remove {{ itemLabel }} {{ index + 1 }}</span>
          </Button>
        </div>
        <p v-if="props.errors?.[index]" :id="`${id}-error-${index}`" role="alert" class="text-xs text-destructive">
          {{ props.errors[index] }}
        </p>
      </li>
    </ul>
    <div>
      <Button ref="addButton" type="button" variant="outline" size="sm" :disabled="disabled" @click="add">
        <PlusIcon />
        {{ addLabel }}
      </Button>
    </div>
  </div>
</template>
