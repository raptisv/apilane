<script setup lang="ts">
import { ArrowLeftIcon, CheckCheckIcon, CircleAlertIcon, LinkIcon } from '@lucide/vue'
import { nextTick, reactive, ref, useId, useTemplateRef, watch } from 'vue'
import FormField from '@/components/FormField.vue'
import { Alert, AlertDescription } from '@/components/ui/alert'
import { Button } from '@/components/ui/button'
import { Checkbox } from '@/components/ui/checkbox'
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from '@/components/ui/dialog'
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'
import type { Schemas } from '@/lib/api'
import { changesOnDelete, isDuplicate, onDeleteHelp } from '@/lib/constraints'
import type { ConstraintRow } from '@/lib/constraints'
import { onDeleteText } from '@/lib/entities'
import type { Constraint } from '@/lib/entities'

// The 'Add constraint' dialog of the constraints screen, in two steps: first the kind of
// constraint, then only the fields of that kind. It saves nothing: it hands the new constraint to
// the screen (`add`), which keeps it until Save.
const props = defineProps<{
  candidates: Schemas['ConstraintCandidatesResponse']
  /** The list as it is on screen, to refuse a constraint the entity already has. */
  rows: ConstraintRow[]
}>()

const emit = defineEmits<{ add: [constraint: Constraint] }>()

const open = defineModel<boolean>('open', { required: true })

const step = ref<'type' | 'Unique' | 'ForeignKey'>('type')
const uniqueProperties = ref<string[]>([])
const foreignKey = reactive({ Property: '', ForeignEntity: '', OnDelete: 'ON_DELETE_NO_ACTION' })

// Shown above the fields after 'Add constraint' was pressed with something missing.
const error = ref<string>()

// The message goes once the user changes a foreign key field.
watch(foreignKey, () => (error.value = undefined))

const content = useTemplateRef<HTMLElement>('content')
const propertiesLabelId = useId()

// Every opening starts at the first step with empty fields.
watch(open, (isOpen) => {
  if (isOpen) {
    step.value = 'type'
    uniqueProperties.value = []
    Object.assign(foreignKey, { Property: '', ForeignEntity: '', OnDelete: 'ON_DELETE_NO_ACTION' })
    error.value = undefined
  }
})

// The step replaces what had the focus: move it to the first control of the new step.
async function goTo(next: typeof step.value): Promise<void> {
  step.value = next
  error.value = undefined
  await nextTick()
  content.value?.querySelector<HTMLElement>('[data-first]')?.focus()
}

function toggleProperty(name: string, checked: boolean): void {
  uniqueProperties.value = checked ? [...uniqueProperties.value, name] : uniqueProperties.value.filter((x) => x !== name)
  error.value = undefined
}

function build(): Constraint | string {
  if (step.value === 'Unique') {
    if (uniqueProperties.value.length === 0) {
      return 'Select at least one property.'
    }

    // In the order the entity lists them, not the order they were ticked in.
    const properties = props.candidates.UniqueProperties.filter((name) => uniqueProperties.value.includes(name))

    return { Type: 'Unique', IsSystem: false, Properties: properties }
  }

  if (!foreignKey.Property || !foreignKey.ForeignEntity || !foreignKey.OnDelete) {
    return 'Select the property, the entity it points to and the on-delete action.'
  }

  return { Type: 'ForeignKey', IsSystem: false, Properties: [], ...foreignKey }
}

function submit(): void {
  const constraint = build()

  if (typeof constraint === 'string') {
    error.value = constraint
    return
  }

  if (isDuplicate(props.rows, constraint)) {
    error.value =
      constraint.Type === 'ForeignKey'
        ? `This entity already has a foreign key from ${constraint.Property} to ${constraint.ForeignEntity}. To change its on-delete action, remove it and save, then add it again.`
        : 'This entity already has a unique constraint on these properties.'
    return
  }

  // The table would keep the old action, so the API refuses this within one save.
  if (changesOnDelete(props.rows, constraint)) {
    error.value = 'To change what happens on delete, save the removal of this foreign key first, then add it again.'
    return
  }

  emit('add', constraint)
  open.value = false
}

const choiceClass =
  'flex flex-col items-start gap-1.5 rounded-lg border p-4 text-left transition-colors hover:bg-muted focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring disabled:cursor-not-allowed disabled:opacity-60 disabled:hover:bg-transparent'
</script>

<template>
  <Dialog v-model:open="open">
    <!-- A click outside does not close the dialog: it would throw away what was chosen. -->
    <DialogContent
      class="max-h-[calc(100dvh-2rem)] overflow-y-auto sm:max-w-lg"
      @interact-outside="(event: Event) => event.preventDefault()"
    >
      <div ref="content" class="grid gap-4">
        <template v-if="step === 'type'">
          <DialogHeader class="pr-8">
            <DialogTitle>Add constraint</DialogTitle>
            <DialogDescription>What kind of constraint? It is added to the list and saved with Save.</DialogDescription>
          </DialogHeader>

          <div class="grid gap-3 sm:grid-cols-2">
            <button type="button" data-first :class="choiceClass" @click="goTo('Unique')">
              <span class="flex items-center gap-2 font-medium text-link">
                <CheckCheckIcon class="size-4" aria-hidden="true" />
                Unique
              </span>
              <span class="text-xs text-muted-foreground">
                No two records may have the same value in a property, or the same combination of values in several.
              </span>
            </button>

            <button
              type="button"
              :class="choiceClass"
              :disabled="candidates.ForeignKeyProperties.length === 0"
              @click="goTo('ForeignKey')"
            >
              <span class="flex items-center gap-2 font-medium text-warning">
                <LinkIcon class="size-4" aria-hidden="true" />
                Foreign key
              </span>
              <span class="text-xs text-muted-foreground">
                A property holds the ID of a record of another entity, and that record must exist.
              </span>
              <span v-if="candidates.ForeignKeyProperties.length === 0" class="text-xs text-foreground">
                Not available: it needs a custom Number property with 0 decimal places, and this entity has none.
              </span>
            </button>
          </div>

          <DialogFooter>
            <Button type="button" variant="outline" @click="open = false">Cancel</Button>
          </DialogFooter>
        </template>

        <form v-else class="grid gap-4" novalidate @submit.prevent="submit">
          <DialogHeader class="pr-8">
            <DialogTitle>{{ step === 'Unique' ? 'Add unique constraint' : 'Add foreign key' }}</DialogTitle>
            <DialogDescription>
              {{
                step === 'Unique'
                  ? 'Select the properties whose values, taken together, must be different in every record.'
                  : 'Select the property that points to another entity, and what happens when the record it points to is deleted.'
              }}
            </DialogDescription>
          </DialogHeader>

          <Alert v-if="error" variant="destructive" role="alert">
            <CircleAlertIcon />
            <AlertDescription>{{ error }}</AlertDescription>
          </Alert>

          <div v-if="step === 'Unique'" class="grid gap-1.5">
            <p :id="propertiesLabelId" class="text-sm font-medium">Properties</p>
            <!-- Clipped sideways, with room around the boxes: their enlarged click area and focus ring stay inside. -->
            <div
              role="group"
              :aria-labelledby="propertiesLabelId"
              class="grid max-h-60 gap-3 overflow-x-hidden overflow-y-auto rounded-lg border px-3.5 py-3"
            >
              <div v-for="(name, index) in candidates.UniqueProperties" :key="name" class="flex items-center gap-2.5">
                <Checkbox
                  :id="`${propertiesLabelId}-${index}`"
                  :data-first="index === 0 ? '' : undefined"
                  :model-value="uniqueProperties.includes(name)"
                  @update:model-value="(checked) => toggleProperty(name, checked === true)"
                />
                <label :for="`${propertiesLabelId}-${index}`" class="min-w-0 font-mono text-sm wrap-anywhere">{{ name }}</label>
              </div>
            </div>
            <p class="text-xs text-muted-foreground">Encrypted properties cannot be unique and are not listed.</p>
          </div>

          <template v-else>
            <FormField v-slot="{ field }" label="Property" help="A custom Number property with 0 decimal places.">
              <Select v-model="foreignKey.Property">
                <SelectTrigger v-bind="field" data-first class="w-full">
                  <SelectValue placeholder="Select a property" />
                </SelectTrigger>
                <SelectContent>
                  <SelectItem v-for="name in candidates.ForeignKeyProperties" :key="name" :value="name">{{ name }}</SelectItem>
                </SelectContent>
              </Select>
            </FormField>

            <FormField v-slot="{ field }" label="Foreign key to entity" help="The property holds the ID of a record of this entity.">
              <Select v-model="foreignKey.ForeignEntity">
                <SelectTrigger v-bind="field" class="w-full">
                  <SelectValue placeholder="Select an entity" />
                </SelectTrigger>
                <SelectContent>
                  <SelectItem v-for="name in candidates.ForeignEntities" :key="name" :value="name">{{ name }}</SelectItem>
                </SelectContent>
              </Select>
            </FormField>

            <FormField v-slot="{ field }" label="On delete behavior" :help="onDeleteHelp[foreignKey.OnDelete]">
              <Select v-model="foreignKey.OnDelete">
                <SelectTrigger v-bind="field" class="w-full">
                  <SelectValue />
                </SelectTrigger>
                <SelectContent>
                  <SelectItem v-for="action in candidates.OnDeleteActions" :key="action" :value="action">
                    On delete {{ onDeleteText(action) }}
                  </SelectItem>
                </SelectContent>
              </Select>
            </FormField>
          </template>

          <DialogFooter class="sm:justify-between">
            <Button type="button" variant="outline" @click="goTo('type')">
              <ArrowLeftIcon />
              Back
            </Button>
            <Button type="submit">Add constraint</Button>
          </DialogFooter>
        </form>
      </div>
    </DialogContent>
  </Dialog>
</template>
