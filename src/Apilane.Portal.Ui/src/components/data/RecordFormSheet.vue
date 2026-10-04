<script setup lang="ts">
import { CircleAlertIcon, InfoIcon, Loader2Icon } from '@lucide/vue'
import { computed, nextTick, reactive, ref, shallowRef, useTemplateRef, watch } from 'vue'
import FormField from '@/components/FormField.vue'
import { Alert, AlertDescription } from '@/components/ui/alert'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'
import { Sheet, SheetContent, SheetDescription, SheetFooter, SheetHeader, SheetTitle } from '@/components/ui/sheet'
import { useMutation } from '@/composables/useMutation'
import { apiServer } from '@/lib/apiServer'
import type { Entity } from '@/lib/entities'
import type { Property } from '@/lib/properties'
import {
  cellText,
  createBody,
  dateFormat,
  formProperties,
  initialForm,
  isUsers,
  primaryKey,
  recordErrors,
  updateBody,
} from '@/lib/records'
import type { DataApplication, DataRecord, RecordForm } from '@/lib/records'

// Creates or edits one record of an entity, in a side sheet with one box per property a caller may
// set (AllowEdit), generated from the property metadata. A new user of the Users entity registers
// through Account/Register of the API server. An edit sends only the boxes that changed.
// Problems the API server reports at a property show under its box.
const props = defineProps<{
  application: DataApplication
  entity: Entity
  /** The record to edit, as the grid has it; none for a new record. */
  record?: DataRecord
  /** The application is someone else's (see EntityDataBrowser): no link to its Email screen. */
  foreign?: boolean
}>()

const emit = defineEmits<{ saved: [] }>()

const open = defineModel<boolean>('open', { required: true })

const server = computed(() => apiServer(props.application.Server.ServerUrl, props.application.Token))
const creating = computed(() => props.record === undefined)
const key = computed(() => primaryKey(props.entity.Properties ?? []))
const fields = computed(() => formProperties(props.entity, creating.value))

// The values the server sets itself (ID, Owner, Created ...), shown read-only above the form of an edit.
const readOnly = computed(() =>
  props.record ? (props.entity.Properties ?? []).filter((property) => !property.AllowEdit) : [],
)

const form = reactive<RecordForm>({})
// The boxes as they were filled, to find what the user changed.
let initial: RecordForm = {}
const nothingChanged = ref(false)

const save = useMutation(async () => {
  const name = props.entity.Name

  if (!props.record) {
    const body = createBody(fields.value, form)

    return isUsers(props.entity)
      ? server.value.post<number>('/api/Account/Register', body)
      : server.value.post<number[]>('/api/Data/Post', body, { entity: name })
  }

  const body = updateBody(fields.value, initial, form, key.value, props.record[key.value])

  return body ? server.value.put<number>('/api/Data/Put', body, { entity: name }) : undefined
})

const errors = computed(() => recordErrors(save.error.value, fields.value.map((property) => property.Name)))

watch(open, (isOpen) => {
  if (isOpen) {
    initial = initialForm(fields.value, props.record, new Date())

    for (const name of Object.keys(form)) {
      delete form[name]
    }

    Object.assign(form, initial)
    nothingChanged.value = false
    save.reset()
  }
})

const formEl = useTemplateRef<HTMLFormElement>('formEl')
const pending = shallowRef(false)

async function onSubmit(): Promise<void> {
  if (pending.value) {
    return
  }

  if (props.record && !updateBody(fields.value, initial, form, key.value, props.record[key.value])) {
    nothingChanged.value = true
    return
  }

  nothingChanged.value = false
  pending.value = true

  try {
    if (await save.run()) {
      open.value = false
      emit('saved')
    } else {
      // Move to the first box the API server rejected.
      await nextTick()
      formEl.value?.querySelector<HTMLElement>('[aria-invalid="true"]')?.focus()
    }
  } finally {
    pending.value = false
  }
}

// Escape and the close button do nothing while the save is running.
function setOpen(value: boolean): void {
  if (!pending.value) {
    open.value = value
  }
}

function label(property: Property): string {
  return property.Required ? `${property.Name} (required)` : property.Name
}

function help(property: Property): string | undefined {
  const parts: string[] = []

  if (property.Description) {
    parts.push(property.Description)
  }

  if (property.Type === 'Date') {
    // A new record is filled with local time, but the value is read as UTC.
    parts.push(
      creating.value ? `Format ${dateFormat}, read as UTC. Filled with your local time now.` : `Format ${dateFormat}, in UTC.`,
    )
  }

  if (property.Encrypted) {
    parts.push('Stored encrypted.')
  }

  return parts.length > 0 ? parts.join(' ') : undefined
}

function numberStep(property: Property): string {
  const places = property.DecimalPlaces ?? 0
  return places > 0 ? `0.${'0'.repeat(places - 1)}1` : '1'
}

// The Boolean select cannot hold '' (no value): 'null' stands for it.
function booleanValue(property: Property): string {
  const value = form[property.Name]
  return value === '' || value === undefined ? 'null' : String(value)
}

function setBoolean(property: Property, value: unknown): void {
  form[property.Name] = value === 'null' ? '' : String(value)
}

const title = computed(() =>
  creating.value
    ? isUsers(props.entity)
      ? 'Register user'
      : `New record for ${props.entity.Name}`
    : `Edit record ${String(props.record?.[key.value] ?? '')}`,
)
</script>

<template>
  <Sheet :open="open" @update:open="setOpen">
    <!-- A click outside does not close the sheet: it would throw away what was typed. -->
    <SheetContent
      class="w-full gap-0 data-[side=right]:w-full data-[side=right]:sm:max-w-lg"
      @interact-outside="(event: Event) => event.preventDefault()"
    >
      <form ref="formEl" class="flex min-h-0 flex-1 flex-col" novalidate @submit.prevent="onSubmit">
        <SheetHeader class="border-b pr-12">
          <SheetTitle class="wrap-anywhere">{{ title }}</SheetTitle>
          <SheetDescription>
            {{ creating ? 'Empty boxes are saved as null.' : 'Only the boxes you change are saved. An empty box is saved as null.' }}
          </SheetDescription>
        </SheetHeader>

        <div class="grid flex-1 content-start gap-4 overflow-y-auto p-4">
          <Alert v-if="errors.message" variant="destructive" role="alert">
            <CircleAlertIcon />
            <AlertDescription class="wrap-anywhere">{{ errors.message }}</AlertDescription>
          </Alert>

          <dl v-if="readOnly.length > 0" class="grid grid-cols-[auto_1fr] gap-x-4 gap-y-1 rounded-lg border bg-muted/30 p-3 text-xs">
            <template v-for="property in readOnly" :key="property.Name">
              <dt class="text-muted-foreground">{{ property.Name }}</dt>
              <dd class="min-w-0 font-mono wrap-anywhere">
                <template v-if="cellText(property, record?.[property.Name]) !== null">{{ cellText(property, record?.[property.Name]) }}</template>
                <span v-else class="text-muted-foreground italic">null</span>
              </dd>
            </template>
          </dl>

          <p v-if="fields.length === 0" class="text-sm text-muted-foreground">This entity has no properties that can be set.</p>

          <FormField
            v-for="property in fields"
            :key="property.Name"
            v-slot="{ field }"
            :label="label(property)"
            :help="help(property)"
            :error="errors.fields[property.Name]"
          >
            <Select
              v-if="property.Type === 'Boolean'"
              :model-value="booleanValue(property)"
              @update:model-value="(value) => setBoolean(property, value)"
            >
              <SelectTrigger v-bind="field" class="w-full">
                <SelectValue />
              </SelectTrigger>
              <SelectContent>
                <SelectItem value="null">-- select --</SelectItem>
                <SelectItem value="true">true</SelectItem>
                <SelectItem value="false">false</SelectItem>
              </SelectContent>
            </Select>
            <Input
              v-else-if="property.Type === 'Number'"
              v-model="form[property.Name]"
              v-bind="field"
              type="number"
              :step="numberStep(property)"
              inputmode="decimal"
            />
            <Input
              v-else-if="property.Type === 'Date'"
              v-model="form[property.Name]"
              v-bind="field"
              class="font-mono"
              :placeholder="dateFormat"
              autocomplete="off"
              spellcheck="false"
            />
            <Input v-else v-model="form[property.Name]" v-bind="field" autocomplete="off" />
          </FormField>

          <div v-if="creating && isUsers(entity)" class="flex gap-2.5 rounded-lg border border-link/40 bg-link/10 p-3 text-sm">
            <InfoIcon class="mt-0.5 size-4 shrink-0 text-link" aria-hidden="true" />
            <p>
              If the application sends a confirmation email on register, the new user receives one to confirm the
              email address.
              <template v-if="!foreign">
                See the application's
                <RouterLink
                  :to="{ name: 'app-email', params: { appToken: application.Token } }"
                  target="_blank"
                  class="text-link underline underline-offset-4"
                >
                  Email settings<span class="sr-only"> (opens in a new tab)</span>
                </RouterLink>.
              </template>
            </p>
          </div>
        </div>

        <SheetFooter class="flex-row justify-end border-t">
          <p v-if="nothingChanged" role="status" class="mr-auto self-center text-sm text-muted-foreground">Nothing has changed.</p>
          <Button type="button" variant="outline" :disabled="pending" @click="setOpen(false)">Cancel</Button>
          <Button type="submit" :disabled="pending">
            <Loader2Icon v-if="pending" class="animate-spin" />
            {{ creating ? (isUsers(entity) ? 'Register' : 'Create') : 'Save' }}
          </Button>
        </SheetFooter>
      </form>
    </SheetContent>
  </Sheet>
</template>
