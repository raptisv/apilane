<script setup lang="ts">
import { ColumnsIcon, PlusIcon, TriangleAlertIcon } from '@lucide/vue'
import { computed, reactive, ref } from 'vue'
import ConfirmByNameDialog from '@/components/ConfirmByNameDialog.vue'
import FormDialog from '@/components/FormDialog.vue'
import FormField from '@/components/FormField.vue'
import StateMessage from '@/components/StateMessage.vue'
import SwitchField from '@/components/SwitchField.vue'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'
import { useApplication } from '@/composables/useApplication'
import { useEntity } from '@/composables/useEntity'
import { useMutation } from '@/composables/useMutation'
import { api, unwrap } from '@/lib/api'
import { formErrors } from '@/lib/forms'
import { decimalPlacesText, limitNoun, maxLengthNote, numberOrNull, propertyTypes, typeFields } from '@/lib/properties'
import type { Property } from '@/lib/properties'
import * as toast from '@/lib/toast'
import PropertyTable from './PropertyTable.vue'

const { application } = useApplication()
const { entity, reload } = useEntity()

// AppLayout and EntityLayout give every application and entity a fresh screen, so both are fixed
// for the life of this one.
const appToken = application.value.Token
const entityName = entity.value.Name
const entityPath = { appToken, entity: entityName }

function propertyPath(property: Property) {
  return { params: { path: { ...entityPath, property: property.Name } } }
}

// The entity answer carries the properties, so they are not loaded again: `reload()` after a write
// reloads the entity. One list: the primary key, then the custom properties, then the system ones.
const properties = computed(() => entity.value.Properties ?? [])
const custom = computed(() => properties.value.filter((property) => !property.IsSystem))
const system = computed(() => properties.value.filter((property) => property.IsSystem))

const nameHelp = "Letters (a-z, A-Z) and underscore only, 4 to 120 characters. It cannot end with '_Data'."
const regexHelp = 'A regular expression every value must match. Leave it empty for no check.'
const wholeNumberHelp = 'A whole number.'
const decimalPlaceOptions = [0, 1, 2, 3, 4, 5, 6, 7, 8]

// New property. A number box holds a number, or '' while it is empty.
const emptyCreateForm = {
  Type: '',
  Name: '',
  Description: '',
  Required: false,
  Encrypted: false,
  ValidationRegex: '',
  DecimalPlaces: '',
  Minimum: '' as string | number,
  Maximum: '' as string | number,
}

const createOpen = ref(false)
const createForm = reactive({ ...emptyCreateForm })

// The form shows only the fields the chosen type uses.
const createFields = computed(() => typeFields(createForm.Type))
const createLimit = computed(() => limitNoun(createForm.Type))
const createMaxNote = computed(() => (createForm.Type === 'String' ? maxLengthNote(application.value.DatabaseType) : undefined))

const create = useMutation(() => {
  const fields = createFields.value

  return unwrap(
    api.POST('/api/v1/applications/{appToken}/entities/{entity}/properties', {
      params: { path: entityPath },
      body: {
        Name: createForm.Name,
        Description: createForm.Description.trim() || null,
        Type: createForm.Type,
        Required: createForm.Required,
        // A value typed before the type was changed must not travel with a type that has no field for it.
        Encrypted: fields.encrypted && createForm.Encrypted,
        // As typed: a space at either end of a pattern is part of it.
        ValidationRegex: fields.validationRegex ? createForm.ValidationRegex || null : null,
        DecimalPlaces: fields.decimalPlaces ? numberOrNull(createForm.DecimalPlaces) : null,
        Minimum: fields.minMax ? numberOrNull(createForm.Minimum) : null,
        Maximum: fields.minMax ? numberOrNull(createForm.Maximum) : null,
      },
    }),
  )
})
const createErrors = computed(() =>
  formErrors(create.error.value, [
    'Name',
    'Description',
    'Type',
    'Required',
    'Encrypted',
    'ValidationRegex',
    'DecimalPlaces',
    'Minimum',
    'Maximum',
  ]),
)

function openCreate(): void {
  Object.assign(createForm, emptyCreateForm)
  create.reset()
  createOpen.value = true
}

async function submitCreate(): Promise<boolean> {
  if (!(await create.run())) {
    return false
  }

  toast.success(`Property ${createForm.Name.trim()} created.`)
  void reload()
  return true
}

// Edit. `editing`, `renaming` and `deleting` keep their value after the dialog closes, so the
// text does not change while it fades out.
const editOpen = ref(false)
const editing = ref<Property>()
const editForm = reactive({ Description: '', ValidationRegex: '', Minimum: '' as string | number, Maximum: '' as string | number })

const update = useMutation((property: Property) =>
  unwrap(
    api.PUT('/api/v1/applications/{appToken}/entities/{entity}/properties/{property}', {
      ...propertyPath(property),
      // The API writes only what the stored type allows and ignores the rest.
      body: {
        Description: editForm.Description.trim() || null,
        ValidationRegex: property.AllowValidationRegex ? editForm.ValidationRegex || null : null,
        Minimum: property.AllowMin ? numberOrNull(editForm.Minimum) : null,
        Maximum: property.AllowMaxEdit ? numberOrNull(editForm.Maximum) : null,
      },
    }),
  ),
)
const editErrors = computed(() => formErrors(update.error.value, ['Description', 'ValidationRegex', 'Minimum', 'Maximum']))

// What was fixed when the property was created, for the line under the type.
function fixedText(property: Property): string {
  const fixed = [property.Required ? 'Required' : 'Not required']

  if (property.Type === 'String') {
    fixed.push(property.Encrypted ? 'Encrypted' : 'Not encrypted')
  }

  if (property.Type === 'Number' && property.DecimalPlaces != null) {
    fixed.push(decimalPlacesText(property.DecimalPlaces))
  }

  return `${fixed.join(' · ')}. These and the type cannot be changed after the property is created.`
}

function openEdit(property: Property): void {
  editing.value = property
  editForm.Description = property.Description ?? ''
  editForm.ValidationRegex = property.ValidationRegex ?? ''
  editForm.Minimum = property.Minimum ?? ''
  editForm.Maximum = property.Maximum ?? ''
  update.reset()
  editOpen.value = true
}

async function submitEdit(): Promise<boolean> {
  if (!editing.value || !(await update.run(editing.value))) {
    return false
  }

  toast.success('Property saved.')
  void reload()
  return true
}

// Rename
const renameOpen = ref(false)
const renaming = ref<Property>()
const renameForm = reactive({ NewName: '' })

const rename = useMutation((property: Property) =>
  unwrap(
    api.POST('/api/v1/applications/{appToken}/entities/{entity}/properties/{property}/rename', {
      ...propertyPath(property),
      body: renameForm,
    }),
  ),
)
const renameErrors = computed(() => formErrors(rename.error.value, ['NewName']))

function openRename(property: Property): void {
  renaming.value = property
  renameForm.NewName = property.Name
  rename.reset()
  renameOpen.value = true
}

async function submitRename(): Promise<boolean> {
  if (!renaming.value || !(await rename.run(renaming.value))) {
    return false
  }

  toast.success('Property renamed.')
  void reload()
  return true
}

// Delete
const deleteOpen = ref(false)
const deleting = ref<Property>()

const remove = useMutation((property: Property) =>
  unwrap(api.DELETE('/api/v1/applications/{appToken}/entities/{entity}/properties/{property}', propertyPath(property))),
)

function openDelete(property: Property): void {
  deleting.value = property
  remove.reset()
  deleteOpen.value = true
}

async function submitDelete(): Promise<boolean> {
  if (!deleting.value || !(await remove.run(deleting.value))) {
    return false
  }

  toast.success('Property deleted.')
  void reload()
  return true
}

const warningClass = 'flex gap-2.5 rounded-lg border border-warning/40 bg-warning/10 p-3 text-sm text-warning'
</script>

<template>
  <!--
    No loading or error state of its own: the properties come with the entity, which EntityLayout loads.
    gap, not margins: the first section is missing for an entity that takes no properties.
  -->
  <div class="flex flex-col gap-8">
    <!-- An entity that takes no new properties (Files) has no custom ones either. -->
    <section v-if="entity.AllowAddProperties || custom.length > 0" aria-labelledby="custom-properties">
      <div class="mb-3 flex flex-wrap items-center justify-between gap-x-4 gap-y-2">
        <h2 id="custom-properties" class="text-sm font-semibold">Custom properties</h2>
        <Button v-if="entity.AllowAddProperties && custom.length > 0" size="sm" @click="openCreate">
          <PlusIcon />
          New property
        </Button>
      </div>

      <StateMessage
        v-if="custom.length === 0"
        :icon="ColumnsIcon"
        title="No custom properties yet"
        description="Add a property for each value a record of this entity holds."
      >
        <Button @click="openCreate">
          <PlusIcon />
          New property
        </Button>
      </StateMessage>

      <PropertyTable v-else :properties="custom" @edit="openEdit" @rename="openRename" @delete="openDelete" />
    </section>

    <section v-if="system.length > 0" aria-labelledby="system-properties">
      <h2 id="system-properties" class="mb-3 text-sm font-semibold">System properties</h2>
      <PropertyTable :properties="system" />
    </section>
  </div>

  <FormDialog
    v-model:open="createOpen"
    title="New property"
    :description="`A new column of entity ${entityName}.`"
    submit-label="Create property"
    :submit="submitCreate"
    :error="createErrors.message"
  >
    <FormField v-slot="{ field }" label="Type" help="It cannot be changed later." :error="createErrors.fields.Type">
      <Select v-model="createForm.Type">
        <SelectTrigger v-bind="field" class="w-full">
          <SelectValue placeholder="Select a type" />
        </SelectTrigger>
        <SelectContent>
          <SelectItem v-for="type in propertyTypes" :key="type" :value="type">{{ type }}</SelectItem>
        </SelectContent>
      </Select>
    </FormField>

    <FormField v-slot="{ field }" label="Name" :help="`${nameHelp} It becomes the name of the column.`" :error="createErrors.fields.Name">
      <Input v-model="createForm.Name" v-bind="field" autocomplete="off" spellcheck="false" maxlength="120" />
    </FormField>

    <FormField v-slot="{ field }" label="Description (optional)" :error="createErrors.fields.Description">
      <Input v-model="createForm.Description" v-bind="field" autocomplete="off" />
    </FormField>

    <FormField
      v-if="createFields.decimalPlaces"
      v-slot="{ field }"
      label="Decimal places"
      help="0 stores whole numbers. It cannot be changed later."
      :error="createErrors.fields.DecimalPlaces"
    >
      <Select v-model="createForm.DecimalPlaces">
        <SelectTrigger v-bind="field" class="w-full">
          <SelectValue placeholder="Select the decimal places" />
        </SelectTrigger>
        <SelectContent>
          <SelectItem v-for="places in decimalPlaceOptions" :key="places" :value="String(places)">{{ places }}</SelectItem>
        </SelectContent>
      </Select>
    </FormField>

    <FormField
      v-if="createFields.validationRegex"
      v-slot="{ field }"
      label="Validation regex (optional)"
      :help="regexHelp"
      :error="createErrors.fields.ValidationRegex"
    >
      <Input v-model="createForm.ValidationRegex" v-bind="field" class="font-mono" autocomplete="off" spellcheck="false" />
    </FormField>

    <div v-if="createFields.minMax" class="grid items-start gap-4 sm:grid-cols-2">
      <FormField
        v-slot="{ field }"
        :label="`Minimum ${createLimit} (optional)`"
        :help="createForm.Type === 'Number' ? wholeNumberHelp : undefined"
        :error="createErrors.fields.Minimum"
      >
        <Input v-model="createForm.Minimum" v-bind="field" type="number" step="1" inputmode="numeric" />
      </FormField>

      <FormField
        v-slot="{ field }"
        :label="`Maximum ${createLimit} (optional)`"
        :help="createForm.Type === 'String' ? 'The size of the column. It cannot be changed later.' : wholeNumberHelp"
        :error="createErrors.fields.Maximum"
      >
        <Input v-model="createForm.Maximum" v-bind="field" type="number" step="1" inputmode="numeric" />
      </FormField>
    </div>

    <div v-if="createMaxNote" :class="warningClass">
      <TriangleAlertIcon class="mt-0.5 size-4 shrink-0" aria-hidden="true" />
      <p>{{ createMaxNote }}</p>
    </div>

    <SwitchField
      v-if="createFields.encrypted"
      v-model="createForm.Encrypted"
      label="Encrypted"
      help="Values are stored encrypted. It cannot be changed later."
      :error="createErrors.fields.Encrypted"
    />

    <SwitchField
      v-model="createForm.Required"
      label="Required"
      help="A record must have a value for it. It cannot be changed later."
      :error="createErrors.fields.Required"
    />
  </FormDialog>

  <FormDialog
    v-if="editing"
    v-model:open="editOpen"
    :title="`Edit property ${editing.Name}`"
    :submit="submitEdit"
    :error="editErrors.message"
  >
    <FormField v-slot="{ field }" label="Description (optional)" :error="editErrors.fields.Description">
      <Input v-model="editForm.Description" v-bind="field" autocomplete="off" />
    </FormField>

    <FormField v-slot="{ field }" label="Type" :help="fixedText(editing)">
      <Input :model-value="editing.Type" v-bind="field" disabled />
    </FormField>

    <FormField
      v-if="editing.AllowValidationRegex"
      v-slot="{ field }"
      label="Validation regex (optional)"
      :help="regexHelp"
      :error="editErrors.fields.ValidationRegex"
    >
      <Input v-model="editForm.ValidationRegex" v-bind="field" class="font-mono" autocomplete="off" spellcheck="false" />
    </FormField>

    <!-- The maximum of a String shows next to its minimum, but it is the size of the column: read-only. -->
    <div v-if="editing.AllowMin || editing.AllowMaxEdit" class="grid items-start gap-4 sm:grid-cols-2">
      <FormField
        v-if="editing.AllowMin"
        v-slot="{ field }"
        :label="`Minimum ${limitNoun(editing.Type)} (optional)`"
        :help="editing.Type === 'Number' ? wholeNumberHelp : undefined"
        :error="editErrors.fields.Minimum"
      >
        <Input v-model="editForm.Minimum" v-bind="field" type="number" step="1" inputmode="numeric" />
      </FormField>

      <FormField
        v-if="editing.AllowMaxEdit"
        v-slot="{ field }"
        :label="`Maximum ${limitNoun(editing.Type)} (optional)`"
        :help="wholeNumberHelp"
        :error="editErrors.fields.Maximum"
      >
        <Input v-model="editForm.Maximum" v-bind="field" type="number" step="1" inputmode="numeric" />
      </FormField>

      <FormField
        v-else-if="editing.Type === 'String'"
        v-slot="{ field }"
        label="Maximum length"
        help="The size of the column. It cannot be changed."
      >
        <Input :model-value="editing.Maximum ?? ''" v-bind="field" placeholder="No limit" disabled />
      </FormField>
    </div>
  </FormDialog>

  <FormDialog
    v-if="renaming"
    v-model:open="renameOpen"
    :title="`Rename property ${renaming.Name}`"
    submit-label="Rename"
    :submit="submitRename"
    :error="renameErrors.message"
  >
    <FormField v-slot="{ field }" label="New name" :help="nameHelp" :error="renameErrors.fields.NewName">
      <Input v-model="renameForm.NewName" v-bind="field" autocomplete="off" spellcheck="false" maxlength="120" />
    </FormField>

    <!-- Under the field, so the name box and not the link gets the focus when the dialog opens. -->
    <div v-if="application.CustomEndpointCount > 0" :class="warningClass">
      <TriangleAlertIcon class="mt-0.5 size-4 shrink-0" aria-hidden="true" />
      <p>
        Custom endpoints that use this property keep the old name in their SQL and will stop working. After renaming,
        update them in the
        <RouterLink :to="{ name: 'app-endpoints', params: { appToken } }" target="_blank" class="font-medium underline underline-offset-4">
          custom endpoints<span class="sr-only"> (opens in a new tab)</span></RouterLink
        >
        section.
      </p>
    </div>

    <div v-if="entity.RequireChangeTracking" :class="warningClass">
      <TriangleAlertIcon class="mt-0.5 size-4 shrink-0" aria-hidden="true" />
      <p>History records that refer to this property by its old name will no longer be available.</p>
    </div>
  </FormDialog>

  <ConfirmByNameDialog
    v-if="deleting"
    v-model:open="deleteOpen"
    :title="`Delete property ${deleting.Name}?`"
    description="All associated data will be lost. This cannot be undone."
    :name="deleting.Name"
    :action="submitDelete"
    :error="remove.error.value?.message"
  />
</template>
