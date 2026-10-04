<script setup lang="ts">
import { ArrowDownIcon, ArrowDownWideNarrowIcon, ArrowUpIcon, ArrowUpNarrowWideIcon, CircleAlertIcon, ListOrderedIcon, PlusIcon, XIcon } from '@lucide/vue'
import { computed, nextTick, ref, useTemplateRef, watch } from 'vue'
import ErrorState from '@/components/ErrorState.vue'
import FormField from '@/components/FormField.vue'
import LoadingState from '@/components/LoadingState.vue'
import StateMessage from '@/components/StateMessage.vue'
import UnsavedChangesBar from '@/components/UnsavedChangesBar.vue'
import { Alert, AlertDescription } from '@/components/ui/alert'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'
import { useApplication } from '@/composables/useApplication'
import { useAsync } from '@/composables/useAsync'
import { useEntity } from '@/composables/useEntity'
import { useMutation } from '@/composables/useMutation'
import { api, unwrap } from '@/lib/api'
import { directionText, moveItem, remainingCandidates, sameOrder, toggleDirection } from '@/lib/defaultOrder'
import type { SortItem } from '@/lib/defaultOrder'
import { listErrors } from '@/lib/forms'
import * as toast from '@/lib/toast'

// The default sorting of one entity: the order its records come in when a request asks for none.
// The list is edited here and sent as a whole with one Save.
const { application } = useApplication()
const { entity } = useEntity()

// AppLayout and EntityLayout give every application and entity a fresh screen.
const path = { params: { path: { appToken: application.value.Token, entity: entity.value.Name } } }

const { data, error, loading, reload } = useAsync(() =>
  unwrap(api.GET('/api/v1/applications/{appToken}/entities/{entity}/default-order', path)),
)

const saved = computed(() => data.value?.Items ?? [])
const items = ref<SortItem[]>([])

// Each load or save starts the editing over from what is stored.
watch(data, (loaded) => {
  items.value = loaded ? loaded.Items.map((item) => ({ ...item })) : []
})

const dirty = computed(() => !sameOrder(saved.value, items.value))
const remaining = computed(() => remainingCandidates(data.value?.Candidates ?? [], items.value))

const save = useMutation(async () => {
  data.value = await unwrap(
    api.PUT('/api/v1/applications/{appToken}/entities/{entity}/default-order', { ...path, body: { Items: items.value } }),
  )
})

// The API names each problem by the place of the item in the list, which is its place on screen.
const errors = computed(() => listErrors(save.error.value, 'Items'))
const topMessage = computed(
  () => errors.value.message ?? (Object.keys(errors.value.rows).length > 0 ? 'Fix the properties marked below, then save again.' : undefined),
)

// Any edit makes the messages of the last save stale: the places in the list may have moved.
function edit(next: SortItem[]): void {
  items.value = next
  save.reset()
}

const toAdd = ref('')
const addForm = useTemplateRef<HTMLFormElement>('addForm')

// The Add button is disabled again once the property is in the list: the focus goes back to the
// select, ready for the next one.
async function add(): Promise<void> {
  if (toAdd.value && remaining.value.includes(toAdd.value) && !save.pending.value) {
    edit([...items.value, { Property: toAdd.value, Direction: 'asc' }])
    toAdd.value = ''
    await nextTick()
    addForm.value?.querySelector<HTMLElement>('[role="combobox"]')?.focus()
  }
}

const list = useTemplateRef<HTMLElement>('list')

// A moved row takes its buttons along: keep the focus on the button that was pressed, or on the
// other one when the row has reached the end of the list and that button is now disabled.
async function move(index: number, step: -1 | 1): Promise<void> {
  const property = items.value[index].Property
  edit(moveItem(items.value, index, step))
  await nextTick()

  const buttons = [step === -1 ? 'up' : 'down', step === -1 ? 'down' : 'up'].map((direction) =>
    list.value?.querySelector<HTMLButtonElement>(`[data-move="${CSS.escape(`${property}:${direction}`)}"]`),
  )
  buttons.find((button) => button && !button.disabled)?.focus()
}

// The removed row is gone with its button: move the focus to the Remove button now in that place,
// or the one above it, or the select to add a property when the list is empty.
async function remove(index: number): Promise<void> {
  edit(items.value.filter((_, i) => i !== index))
  await nextTick()

  const next =
    list.value?.querySelector<HTMLElement>(`[data-remove="${index}"]`) ??
    list.value?.querySelector<HTMLElement>(`[data-remove="${index - 1}"]`) ??
    addForm.value?.querySelector<HTMLElement>('[role="combobox"]')
  next?.focus()
}

function discard(): void {
  edit(saved.value.map((item) => ({ ...item })))
}

async function submit(): Promise<void> {
  if (await save.run()) {
    toast.success('Default sorting saved.')
  }
}

function isNew(item: SortItem): boolean {
  return !saved.value.some((stored) => stored.Property === item.Property)
}
</script>

<template>
  <div class="max-w-2xl">
    <h2 class="mb-2 text-sm font-semibold">Default sorting</h2>
    <div class="mb-4 space-y-1.5 text-sm text-muted-foreground">
      <p>The order the records of this entity are returned in when a request does not ask for one.</p>
      <p>
        Records are sorted by the first property; the next ones decide between records that have the same values in the
        properties above them.
      </p>
    </div>

    <LoadingState v-if="loading" label="Loading the default sorting" />

    <ErrorState v-else-if="error" :error="error" @retry="reload" />

    <template v-else-if="data">
      <Alert v-if="topMessage" variant="destructive" role="alert" class="mb-4">
        <CircleAlertIcon />
        <AlertDescription>{{ topMessage }}</AlertDescription>
      </Alert>

      <StateMessage
        v-if="items.length === 0"
        :icon="ListOrderedIcon"
        title="No default sorting"
        description="Records are returned by ID, ascending. Add a property below to sort by it instead."
      />

      <ol v-else ref="list" class="grid gap-2" aria-label="Sort by">
        <li
          v-for="(item, index) in items"
          :key="item.Property"
          class="flex flex-wrap items-center gap-x-3 gap-y-2 rounded-lg border px-3 py-2"
          :class="errors.rows[index] ? 'border-destructive/60' : isNew(item) && 'border-success/50 bg-success/5'"
        >
          <span class="w-5 shrink-0 text-right font-mono text-xs text-muted-foreground" aria-hidden="true">{{ index + 1 }}</span>
          <!-- At least this wide: on a phone the buttons move to a line of their own instead of squeezing the name. -->
          <span class="min-w-28 flex-1 font-mono text-sm wrap-anywhere">{{ item.Property }}</span>
          <Badge v-if="isNew(item)" variant="outline" class="border-success/50 text-success">New, not saved</Badge>

          <div class="ml-auto flex shrink-0 items-center gap-1">
            <Button
              variant="outline"
              size="sm"
              class="w-28"
              :title="`Switch to ${directionText(item.Direction === 'asc' ? 'desc' : 'asc')}`"
              :disabled="save.pending.value"
              @click="edit(toggleDirection(items, index))"
            >
              <component :is="item.Direction === 'asc' ? ArrowUpNarrowWideIcon : ArrowDownWideNarrowIcon" />
              <span><span class="sr-only">{{ item.Property }}: </span>{{ directionText(item.Direction) }}<span class="sr-only"
                  >, press to sort {{ directionText(item.Direction === 'asc' ? 'desc' : 'asc') }}</span
                ></span>
            </Button>
            <Button
              variant="ghost"
              size="icon-sm"
              title="Move up"
              :data-move="`${item.Property}:up`"
              :disabled="index === 0 || save.pending.value"
              @click="move(index, -1)"
            >
              <ArrowUpIcon />
              <span class="sr-only">Move {{ item.Property }} up</span>
            </Button>
            <Button
              variant="ghost"
              size="icon-sm"
              title="Move down"
              :data-move="`${item.Property}:down`"
              :disabled="index === items.length - 1 || save.pending.value"
              @click="move(index, 1)"
            >
              <ArrowDownIcon />
              <span class="sr-only">Move {{ item.Property }} down</span>
            </Button>
            <Button variant="ghost" size="icon-sm" title="Remove" :data-remove="index" :disabled="save.pending.value" @click="remove(index)">
              <XIcon />
              <span class="sr-only">Remove {{ item.Property }}</span>
            </Button>
          </div>

          <p v-if="errors.rows[index]" role="alert" class="basis-full text-xs text-destructive">{{ errors.rows[index] }}</p>
        </li>
      </ol>

      <p v-if="remaining.length === 0" class="mt-4 text-sm text-muted-foreground">Every property is in the list.</p>

      <form v-else ref="addForm" class="mt-4 flex flex-wrap items-end gap-2" @submit.prevent="add">
        <FormField v-slot="{ field }" label="Add a property" class="min-w-0 flex-1 basis-56">
          <Select v-model="toAdd">
            <SelectTrigger v-bind="field" class="w-full">
              <SelectValue placeholder="Select a property" />
            </SelectTrigger>
            <SelectContent>
              <SelectItem v-for="name in remaining" :key="name" :value="name">{{ name }}</SelectItem>
            </SelectContent>
          </Select>
        </FormField>
        <!-- Locked while saving, like the list: the save resets it to what was stored. -->
        <Button type="submit" variant="outline" :disabled="!toAdd || save.pending.value">
          <PlusIcon />
          Add
        </Button>
      </form>
    </template>

    <UnsavedChangesBar
      :dirty="dirty"
      :pending="save.pending.value"
      message="The default sorting has unsaved changes."
      @save="submit"
      @discard="discard"
    />
  </div>
</template>
