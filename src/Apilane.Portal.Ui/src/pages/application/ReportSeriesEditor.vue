<script setup lang="ts">
import { ChevronDownIcon, FilterIcon, RefreshCwIcon, Trash2Icon } from '@lucide/vue'
import { computed, ref } from 'vue'
import FilterBuilder from '@/components/FilterBuilder.vue'
import FormDialog from '@/components/FormDialog.vue'
import FormField from '@/components/FormField.vue'
import { Button } from '@/components/ui/button'
import {
  DropdownMenu,
  DropdownMenuCheckboxItem,
  DropdownMenuContent,
  DropdownMenuLabel,
  DropdownMenuTrigger,
} from '@/components/ui/dropdown-menu'
import { Input } from '@/components/ui/input'
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'
import { useAsync } from '@/composables/useAsync'
import { api, unwrap } from '@/lib/api'
import type { Entity } from '@/lib/entities'
import { filterSummary, filterText, parseFilter } from '@/lib/filterBuilder'
import type { FilterRow } from '@/lib/filterBuilder'
import { fieldNames, notOffered, reportTypeLabel, reportTypes, splitGroupBy, toggleGroupBy } from '@/lib/reports'
import type { SeriesForm } from '@/lib/reports'

// One series of a report in the editor: label, entity, what to group by, the value of each group
// and a filter. What can be grouped by and shown depends on the entity and on the type of the
// report: the API lists it (report-fields), and the list is loaded again when either changes.
const props = defineProps<{
  appToken: string
  /** The Type of the report being edited. */
  type: string
  /** The entities of the application, with their properties (for the filter). */
  entities: Entity[]
  /** The place of the series in the list, from 0. */
  index: number
  /** The message of each field of this series from reportErrors (lib/reports.ts); '' is the series as a whole. */
  errors?: Record<string, string>
  disabled?: boolean
}>()

defineEmits<{ remove: [] }>()

const series = defineModel<SeriesForm>({ required: true })

function set(change: Partial<SeriesForm>): void {
  series.value = { ...series.value, ...change }
}

const typeKnown = computed(() => reportTypes.some((item) => item.value === props.type))

const fields = useAsync(
  () =>
    series.value.Entity && typeKnown.value
      ? unwrap(
          api.GET('/api/v1/applications/{appToken}/entities/{entity}/report-fields', {
            params: { path: { appToken: props.appToken, entity: series.value.Entity }, query: { Type: props.type } },
          }),
        )
      : Promise.resolve(undefined),
  { watch: [() => series.value.Entity, () => props.type] },
)

const groupings = computed(() => fields.data.value?.Groupings ?? [])
const offeredGroups = computed(() => fieldNames(groupings.value))
const offeredProperties = computed(() => fieldNames(fields.data.value?.Properties ?? []))

const selectedGroups = computed(() => splitGroupBy(series.value.GroupBy))

// A stored entity or value that is not offered (any more) still shows, so it can be seen and changed.
const entityOptions = computed(() => {
  const names = props.entities.map((entity) => entity.Name)

  return series.value.Entity === '' || names.includes(series.value.Entity) ? names : [series.value.Entity, ...names]
})

const propertyOptions = computed(() =>
  series.value.Property === '' || offeredProperties.value.includes(series.value.Property)
    ? offeredProperties.value
    : [series.value.Property, ...offeredProperties.value],
)

// Changing the type of the report keeps what was chosen, as the classic editor does. What the new
// type does not offer is pointed out here, before the API refuses it.
const notOfferedText = computed(() => `Not offered for a ${reportTypeLabel(props.type)} report on ${series.value.Entity}`)

const propertyProblem = computed(() =>
  fields.data.value && series.value.Property !== '' && !offeredProperties.value.includes(series.value.Property)
    ? `${notOfferedText.value}.`
    : undefined,
)

// A group-by that is not offered is listed in the menu too, ticked, so it can be unticked.
const staleGroups = computed(() => (fields.data.value ? notOffered(offeredGroups.value, series.value.GroupBy) : []))

const groupByProblem = computed(() =>
  staleGroups.value.length > 0 ? `${notOfferedText.value}: ${staleGroups.value.join(', ')}. Untick it in the list.` : undefined,
)

function setEntity(value: unknown): void {
  // The list reports a pick of the entity that is already chosen too: that changes nothing.
  if (String(value) === series.value.Entity) {
    return
  }

  // What was chosen belongs to the old entity.
  set({ Entity: String(value), GroupBy: '', Property: '', Filter: '' })
}

function toggleGroup(name: string): void {
  set({ GroupBy: toggleGroupBy(offeredGroups.value, series.value.GroupBy, name) })
}

// The filter dialog ---------------------------------------------------------------------------

const filterOpen = ref(false)
const filterRows = ref<FilterRow[]>([])
// A stored filter the builder cannot show as conditions (see parseFilter): shown as text, kept unless replaced.
const filterRaw = ref<string>()

const filterProperties = computed(
  () => props.entities.find((entity) => entity.Name === series.value.Entity)?.Properties?.map((property) => property.Name) ?? [],
)

function openFilter(): void {
  const parsed = parseFilter(series.value.Filter)

  filterRows.value = parsed.rows ?? []
  filterRaw.value = parsed.raw
  filterOpen.value = true
}

function applyFilter(): Promise<boolean> {
  if (filterRaw.value === undefined) {
    set({ Filter: filterText(filterRows.value) })
  }

  return Promise.resolve(true)
}
</script>

<template>
  <li class="grid gap-3 rounded-lg border bg-card p-3">
    <div class="flex items-center justify-between gap-2">
      <h4 class="text-sm font-medium">Series {{ index + 1 }}</h4>
      <Button type="button" variant="ghost" size="icon-sm" title="Remove" :disabled="disabled" data-remove-series @click="$emit('remove')">
        <Trash2Icon />
        <span class="sr-only">Remove series {{ index + 1 }}</span>
      </Button>
    </div>

    <p v-if="errors?.['']" role="alert" class="text-xs text-destructive">{{ errors[''] }}</p>

    <div class="grid gap-3 sm:grid-cols-2">
      <FormField v-slot="{ field }" label="Label" :error="errors?.Label">
        <Input
          :model-value="series.Label"
          v-bind="field"
          placeholder="e.g. Paid"
          autocomplete="off"
          :disabled="disabled"
          data-series-label
          @update:model-value="(value) => set({ Label: String(value) })"
        />
      </FormField>

      <FormField v-slot="{ field }" label="Entity" :error="errors?.Entity">
        <Select :model-value="series.Entity" :disabled="disabled" @update:model-value="setEntity">
          <SelectTrigger v-bind="field" class="w-full">
            <SelectValue placeholder="Select an entity" />
          </SelectTrigger>
          <SelectContent>
            <SelectItem v-for="name in entityOptions" :key="name" :value="name">{{ name }}</SelectItem>
          </SelectContent>
        </Select>
      </FormField>

      <FormField
        v-slot="{ field }"
        label="Group by"
        :error="errors?.GroupBy ?? groupByProblem"
        :help="fields.data.value && groupings.length === 0 ? 'This entity has nothing to group by for this type of report.' : undefined"
      >
        <DropdownMenu>
          <DropdownMenuTrigger as-child>
            <Button
              v-bind="field"
              type="button"
              variant="outline"
              class="w-full min-w-0 justify-between font-normal"
              :disabled="disabled || !fields.data.value || (groupings.length === 0 && staleGroups.length === 0)"
            >
              <span v-if="series.GroupBy" class="min-w-0 truncate font-mono text-xs" :title="series.GroupBy">{{ series.GroupBy }}</span>
              <span v-else class="text-muted-foreground">{{ fields.loading.value ? 'Loading…' : 'Select what to group by' }}</span>
              <ChevronDownIcon class="text-muted-foreground" />
            </Button>
          </DropdownMenuTrigger>
          <!-- Ticking keeps the menu open: several can be chosen in one go. -->
          <DropdownMenuContent align="start" class="max-h-72 w-64 overflow-y-auto">
            <template v-if="staleGroups.length > 0">
              <DropdownMenuLabel>Not offered</DropdownMenuLabel>
              <DropdownMenuCheckboxItem
                v-for="name in staleGroups"
                :key="name"
                :model-value="true"
                class="text-destructive"
                @update:model-value="toggleGroup(name)"
                @select.prevent
              >
                {{ name }}
              </DropdownMenuCheckboxItem>
            </template>

            <template v-for="grouping in groupings" :key="grouping.Name">
              <DropdownMenuCheckboxItem
                v-if="grouping.Subs.length === 0"
                :model-value="selectedGroups.includes(grouping.Name)"
                @update:model-value="toggleGroup(grouping.Name)"
                @select.prevent
              >
                {{ grouping.Name }}
              </DropdownMenuCheckboxItem>
              <template v-else>
                <DropdownMenuLabel>{{ grouping.Name }} by</DropdownMenuLabel>
                <DropdownMenuCheckboxItem
                  v-for="sub in grouping.Subs"
                  :key="sub"
                  class="pl-4"
                  :model-value="selectedGroups.includes(`${grouping.Name}.${sub}`)"
                  @update:model-value="toggleGroup(`${grouping.Name}.${sub}`)"
                  @select.prevent
                >
                  {{ sub }}
                </DropdownMenuCheckboxItem>
              </template>
            </template>
          </DropdownMenuContent>
        </DropdownMenu>
      </FormField>

      <FormField
        v-slot="{ field }"
        label="Property"
        :error="errors?.Property ?? propertyProblem"
        :help="fields.data.value && offeredProperties.length === 0 ? 'This entity has nothing to show for this type of report.' : undefined"
      >
        <Select
          :model-value="series.Property"
          :disabled="disabled || !fields.data.value || propertyOptions.length === 0"
          @update:model-value="(value) => set({ Property: String(value) })"
        >
          <SelectTrigger v-bind="field" class="w-full font-mono text-xs">
            <SelectValue :placeholder="fields.loading.value ? 'Loading…' : 'e.g. ID.Count'" />
          </SelectTrigger>
          <SelectContent>
            <SelectItem v-for="name in propertyOptions" :key="name" :value="name" class="font-mono text-xs">{{ name }}</SelectItem>
          </SelectContent>
        </Select>
      </FormField>
    </div>

    <p v-if="!typeKnown" class="text-xs text-muted-foreground">Choose a visualization to see what this series can show.</p>

    <p v-else-if="fields.error.value" role="alert" class="flex flex-wrap items-center gap-x-2 gap-y-1 text-xs text-destructive">
      <span class="min-w-0 wrap-anywhere">Could not load what {{ series.Entity }} offers: {{ fields.error.value.message }}</span>
      <Button type="button" variant="outline" size="xs" @click="fields.reload()">
        <RefreshCwIcon />
        Try again
      </Button>
    </p>

    <FormField v-slot="{ field }" label="Filter" :error="errors?.Filter">
      <div class="flex flex-wrap items-center gap-2">
        <Button v-bind="field" type="button" variant="outline" size="sm" :disabled="disabled || series.Entity === ''" @click="openFilter">
          <FilterIcon />
          {{ filterSummary(series.Filter) }}
        </Button>
        <Button v-if="series.Filter !== ''" type="button" variant="ghost" size="sm" :disabled="disabled" @click="set({ Filter: '' })">
          Remove filter
          <span class="sr-only">of series {{ index + 1 }}</span>
        </Button>
      </div>
    </FormField>

    <FormDialog
      v-model:open="filterOpen"
      :title="`Series filter (${series.Entity})`"
      description="Only the records that meet every condition are counted."
      submit-label="Apply"
      :submit="applyFilter"
    >
      <div v-if="filterRaw !== undefined" class="grid gap-3">
        <p class="text-sm text-muted-foreground">
          This filter was not made with this builder (it has groups, OR, or something else the builder does not show). Apply keeps it
          as it is.
        </p>
        <pre class="max-h-48 overflow-auto rounded-lg border bg-muted/30 p-3 font-mono text-xs wrap-anywhere whitespace-pre-wrap">{{ filterRaw }}</pre>
        <div>
          <Button type="button" variant="outline" size="sm" @click="filterRaw = undefined">Replace it with conditions</Button>
        </div>
      </div>
      <FilterBuilder v-else v-model="filterRows" :properties="filterProperties" />
    </FormDialog>
  </li>
</template>
