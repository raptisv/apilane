<script setup lang="ts">
import { SearchIcon } from '@lucide/vue'
import { computed, ref } from 'vue'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { Select, SelectContent, SelectGroup, SelectItem, SelectLabel, SelectTrigger, SelectValue } from '@/components/ui/select'
import { itemKey } from '@/lib/securityAccess'
import type { SecurityItem } from '@/lib/securityAccess'

// The items of the security screen (the schema, the entities, the custom endpoints), one of them
// selected: a searchable list beside the grid on a wide screen, a select above it on a phone.
const props = defineProps<{
  items: SecurityItem[]
  selected: string
  /** Keys of the items with unsaved changes. */
  changed: Set<string>
  /** Key of the item the last save failed on. */
  failed?: string
}>()

const emit = defineEmits<{ select: [key: string] }>()

const search = ref('')

const groupNames: Record<string, string> = { Schema: 'Schema', Entity: 'Entities', CustomEndpoint: 'Custom endpoints' }

function groups(items: SecurityItem[]) {
  return ['Schema', 'Entity', 'CustomEndpoint']
    .map((type) => ({ type, label: groupNames[type] ?? type, items: items.filter((item) => item.Type === type) }))
    .filter((group) => group.items.length > 0)
}

const allGroups = computed(() => groups(props.items))
const shownGroups = computed(() => {
  const text = search.value.trim().toLowerCase()
  return groups(text === '' ? props.items : props.items.filter((item) => item.Name.toLowerCase().includes(text)))
})

const selectedModel = computed({
  get: () => props.selected,
  set: (key: string) => emit('select', key),
})

const itemClass =
  'flex w-full items-center justify-between gap-2 rounded-md px-2.5 py-1.5 text-left text-sm transition-colors focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring'
</script>

<template>
  <!-- Phone: a select. -->
  <div class="grid gap-1.5 lg:hidden">
    <Label for="security-item-select">Item</Label>
    <Select v-model="selectedModel">
      <SelectTrigger id="security-item-select" class="w-full">
        <SelectValue />
      </SelectTrigger>
      <SelectContent>
        <SelectGroup v-for="group in allGroups" :key="group.type">
          <SelectLabel>{{ group.label }}</SelectLabel>
          <SelectItem v-for="item in group.items" :key="itemKey(item)" :value="itemKey(item)">
            {{ item.Name }}{{ changed.has(itemKey(item)) ? ' (unsaved)' : '' }}
          </SelectItem>
        </SelectGroup>
      </SelectContent>
    </Select>
  </div>

  <!-- Wide screen: a searchable list. -->
  <nav aria-label="Security items" class="hidden lg:grid lg:content-start lg:gap-3">
    <div class="relative">
      <SearchIcon class="pointer-events-none absolute top-1/2 left-2.5 size-4 -translate-y-1/2 text-muted-foreground" aria-hidden="true" />
      <Input v-model="search" type="search" aria-label="Search items" placeholder="Search" class="pl-8" />
    </div>

    <p v-if="shownGroups.length === 0" class="px-2.5 text-sm text-muted-foreground">No item matches.</p>

    <div v-for="group in shownGroups" :key="group.type">
      <h3 class="px-2.5 pb-1 text-xs font-medium text-muted-foreground">{{ group.label }}</h3>
      <ul class="grid gap-0.5">
        <li v-for="item in group.items" :key="itemKey(item)">
          <button
            type="button"
            :class="[itemClass, itemKey(item) === selected ? 'bg-secondary font-medium' : 'text-muted-foreground hover:bg-muted hover:text-foreground']"
            :aria-current="itemKey(item) === selected ? 'true' : undefined"
            @click="emit('select', itemKey(item))"
          >
            <span class="min-w-0 wrap-anywhere">{{ item.Name }}</span>
            <span v-if="itemKey(item) === failed" class="size-2 shrink-0 rounded-full bg-destructive" title="Not saved: see the error" />
            <span v-else-if="changed.has(itemKey(item))" class="size-2 shrink-0 rounded-full bg-warning" title="Unsaved changes" />
            <span v-if="itemKey(item) === failed" class="sr-only">(has an error)</span>
            <span v-else-if="changed.has(itemKey(item))" class="sr-only">(unsaved changes)</span>
          </button>
        </li>
      </ul>
    </div>
  </nav>
</template>
