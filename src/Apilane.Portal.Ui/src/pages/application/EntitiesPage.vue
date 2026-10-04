<script setup lang="ts">
import { PlusIcon, SearchIcon, SearchXIcon, TablePropertiesIcon, TriangleAlertIcon } from '@lucide/vue'
import { computed, reactive, ref } from 'vue'
import ConfirmByNameDialog from '@/components/ConfirmByNameDialog.vue'
import ErrorState from '@/components/ErrorState.vue'
import FormDialog from '@/components/FormDialog.vue'
import FormField from '@/components/FormField.vue'
import LoadingState from '@/components/LoadingState.vue'
import PageHeader from '@/components/PageHeader.vue'
import StateMessage from '@/components/StateMessage.vue'
import SwitchField from '@/components/SwitchField.vue'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { useApplication } from '@/composables/useApplication'
import { useAsync } from '@/composables/useAsync'
import { useMutation } from '@/composables/useMutation'
import { api, unwrap } from '@/lib/api'
import { filterEntities } from '@/lib/entities'
import type { Entity } from '@/lib/entities'
import { formErrors } from '@/lib/forms'
import * as toast from '@/lib/toast'
import EntityTable from './EntityTable.vue'

const { application } = useApplication()

// AppLayout gives every application a fresh screen, so the token is fixed for the life of this one.
const appToken = application.value.Token

function entityPath(entity: Entity) {
  return { params: { path: { appToken, entity: entity.Name } } }
}

const { data, error, loading, reload } = useAsync(() =>
  unwrap(api.GET('/api/v1/applications/{appToken}/entities', { params: { path: { appToken } } })),
)

// The API sends one list ordered by name.
const entities = computed(() => data.value?.Data ?? [])
const custom = computed(() => entities.value.filter((entity) => !entity.IsSystem))
const system = computed(() => entities.value.filter((entity) => entity.IsSystem))

// The search box appears only when there is enough to search through.
const searchFrom = 10
const search = ref('')
const searchable = computed(() => entities.value.length >= searchFrom)
const matching = computed(
  () => new Set(filterEntities(entities.value, searchable.value ? search.value : '').map((entity) => entity.Name)),
)

function anyMatches(list: Entity[]): boolean {
  return list.some((entity) => matching.value.has(entity.Name))
}

const changeTrackingHelp =
  'Keeps a history record of every update of a record. History can be browsed and deleted in the data browser. It slows updates down noticeably: use it only for entities that are not updated often.'

// New entity
const createOpen = ref(false)
const createForm = reactive({ Name: '', Description: '', RequireChangeTracking: false, HasDifferentiationProperty: false })

const create = useMutation(() =>
  unwrap(
    api.POST('/api/v1/applications/{appToken}/entities', {
      params: { path: { appToken } },
      body: {
        Name: createForm.Name,
        Description: createForm.Description.trim() || null,
        RequireChangeTracking: createForm.RequireChangeTracking,
        // The switch exists only for an application with a differentiation entity.
        HasDifferentiationProperty: Boolean(application.value.DifferentiationEntity) && createForm.HasDifferentiationProperty,
      },
    }),
  ),
)
const createErrors = computed(() =>
  formErrors(create.error.value, ['Name', 'Description', 'RequireChangeTracking', 'HasDifferentiationProperty']),
)

function openCreate(): void {
  Object.assign(createForm, { Name: '', Description: '', RequireChangeTracking: false, HasDifferentiationProperty: false })
  create.reset()
  createOpen.value = true
}

async function submitCreate(): Promise<boolean> {
  if (!(await create.run())) {
    return false
  }

  toast.success(`Entity ${createForm.Name.trim()} created.`)
  void reload()
  return true
}

// Edit. `editing`, `renaming` and `deleting` keep their value after the dialog closes, so the
// text does not change while it fades out.
const editOpen = ref(false)
const editing = ref<Entity>()
const editForm = reactive({ Description: '', RequireChangeTracking: false })

const update = useMutation((entity: Entity) =>
  unwrap(
    api.PUT('/api/v1/applications/{appToken}/entities/{entity}', {
      ...entityPath(entity),
      body: {
        Description: editForm.Description.trim() || null,
        // Without the switch (records that cannot be updated) change tracking is off, as in the classic portal.
        RequireChangeTracking: entity.AllowPut && editForm.RequireChangeTracking,
      },
    }),
  ),
)
const editErrors = computed(() => formErrors(update.error.value, ['Description', 'RequireChangeTracking']))

function openEdit(entity: Entity): void {
  editing.value = entity
  editForm.Description = entity.Description ?? ''
  editForm.RequireChangeTracking = entity.RequireChangeTracking
  update.reset()
  editOpen.value = true
}

async function submitEdit(): Promise<boolean> {
  if (!editing.value || !(await update.run(editing.value))) {
    return false
  }

  toast.success('Entity saved.')
  void reload()
  return true
}

// Rename
const renameOpen = ref(false)
const renaming = ref<Entity>()
const renameForm = reactive({ NewName: '' })

const rename = useMutation((entity: Entity) =>
  unwrap(api.POST('/api/v1/applications/{appToken}/entities/{entity}/rename', { ...entityPath(entity), body: renameForm })),
)
const renameErrors = computed(() => formErrors(rename.error.value, ['NewName']))

function openRename(entity: Entity): void {
  renaming.value = entity
  renameForm.NewName = entity.Name
  rename.reset()
  renameOpen.value = true
}

async function submitRename(): Promise<boolean> {
  if (!renaming.value || !(await rename.run(renaming.value))) {
    return false
  }

  toast.success('Entity renamed.')
  void reload()
  return true
}

// Delete
const deleteOpen = ref(false)
const deleting = ref<Entity>()

const remove = useMutation((entity: Entity) =>
  unwrap(api.DELETE('/api/v1/applications/{appToken}/entities/{entity}', entityPath(entity))),
)

function openDelete(entity: Entity): void {
  deleting.value = entity
  remove.reset()
  deleteOpen.value = true
}

async function submitDelete(): Promise<boolean> {
  if (!deleting.value || !(await remove.run(deleting.value))) {
    return false
  }

  toast.success('Entity deleted.')
  void reload()
  return true
}
</script>

<template>
  <PageHeader title="Entities" description="The tables of this application. Each entity holds one kind of record.">
    <Button @click="openCreate">
      <PlusIcon />
      New entity
    </Button>
  </PageHeader>

  <LoadingState v-if="loading" label="Loading entities" />

  <ErrorState v-else-if="error" :error="error" @retry="reload" />

  <template v-else-if="data">
    <div v-if="searchable" class="relative mb-6 sm:max-w-xs">
      <SearchIcon class="pointer-events-none absolute top-1/2 left-2.5 size-4 -translate-y-1/2 text-muted-foreground" />
      <Input v-model="search" type="search" class="pl-8" placeholder="Search by name" aria-label="Search entities by name" />
    </div>

    <StateMessage
      v-if="entities.length > 0 && matching.size === 0"
      :icon="SearchXIcon"
      title="No entity matches"
      :description="`No entity has '${search.trim()}' in its name.`"
    >
      <Button variant="outline" @click="search = ''">Clear search</Button>
    </StateMessage>

    <!-- gap, not margins: it skips the sections a search hides. -->
    <div class="flex flex-col gap-8">
      <section v-show="custom.length === 0 ? matching.size === entities.length : anyMatches(custom)" aria-labelledby="custom-entities">
        <h2 id="custom-entities" class="mb-3 text-sm font-semibold">Custom entities</h2>

        <StateMessage
          v-if="custom.length === 0"
          :icon="TablePropertiesIcon"
          title="No custom entities yet"
          description="Add an entity for each kind of record the application stores."
        >
          <Button @click="openCreate">
            <PlusIcon />
            New entity
          </Button>
        </StateMessage>

        <EntityTable v-else :entities="custom" :matching="matching" @edit="openEdit" @rename="openRename" @delete="openDelete" />
      </section>

      <section v-if="system.length > 0" v-show="anyMatches(system)" aria-labelledby="system-entities">
        <h2 id="system-entities" class="mb-3 text-sm font-semibold">System entities</h2>
        <EntityTable :entities="system" :matching="matching" @edit="openEdit" @rename="openRename" @delete="openDelete" />
      </section>
    </div>
  </template>

  <FormDialog
    v-model:open="createOpen"
    title="New entity"
    submit-label="Create entity"
    :submit="submitCreate"
    :error="createErrors.message"
  >
    <FormField
      v-slot="{ field }"
      label="Name"
      help="Letters (a-z, A-Z) and underscore only, 4 to 30 characters. It becomes the name of the table."
      :error="createErrors.fields.Name"
    >
      <Input v-model="createForm.Name" v-bind="field" autocomplete="off" spellcheck="false" maxlength="30" />
    </FormField>

    <FormField v-slot="{ field }" label="Description (optional)" :error="createErrors.fields.Description">
      <Input v-model="createForm.Description" v-bind="field" autocomplete="off" />
    </FormField>

    <SwitchField
      v-model="createForm.RequireChangeTracking"
      label="Record change tracking"
      :help="changeTrackingHelp"
      :error="createErrors.fields.RequireChangeTracking"
    />

    <SwitchField
      v-if="application.DifferentiationEntity"
      v-model="createForm.HasDifferentiationProperty"
      label="Has differentiation property"
      :help="`Each record belongs to one record of ${application.DifferentiationEntity}. This cannot be changed later.`"
      :error="createErrors.fields.HasDifferentiationProperty"
    />
  </FormDialog>

  <FormDialog
    v-if="editing"
    v-model:open="editOpen"
    :title="`Edit entity ${editing.Name}`"
    :submit="submitEdit"
    :error="editErrors.message"
  >
    <FormField v-slot="{ field }" label="Description (optional)" :error="editErrors.fields.Description">
      <Input v-model="editForm.Description" v-bind="field" autocomplete="off" />
    </FormField>

    <SwitchField
      v-if="editing.AllowPut"
      v-model="editForm.RequireChangeTracking"
      label="Record change tracking"
      :help="changeTrackingHelp"
      :error="editErrors.fields.RequireChangeTracking"
    />
  </FormDialog>

  <FormDialog
    v-if="renaming"
    v-model:open="renameOpen"
    :title="`Rename entity ${renaming.Name}`"
    submit-label="Rename"
    :submit="submitRename"
    :error="renameErrors.message"
  >
    <FormField
      v-slot="{ field }"
      label="New name"
      help="Letters (a-z, A-Z) and underscore only, 4 to 30 characters."
      :error="renameErrors.fields.NewName"
    >
      <Input v-model="renameForm.NewName" v-bind="field" autocomplete="off" spellcheck="false" maxlength="30" />
    </FormField>

    <!-- Under the field, so the name box and not the link gets the focus when the dialog opens. -->
    <div
      v-if="application.CustomEndpointCount > 0"
      class="flex gap-2.5 rounded-lg border border-warning/40 bg-warning/10 p-3 text-sm text-warning"
    >
      <TriangleAlertIcon class="mt-0.5 size-4 shrink-0" aria-hidden="true" />
      <p>
        Custom endpoints that use this entity keep the old name in their SQL and will stop working. After renaming,
        update them in the
        <RouterLink :to="{ name: 'app-endpoints', params: { appToken } }" target="_blank" class="font-medium underline underline-offset-4">
          custom endpoints<span class="sr-only"> (opens in a new tab)</span></RouterLink
        >
        section.
      </p>
    </div>
  </FormDialog>

  <ConfirmByNameDialog
    v-if="deleting"
    v-model:open="deleteOpen"
    :title="`Delete entity ${deleting.Name}?`"
    description="All data of the entity will be lost. This cannot be undone."
    :name="deleting.Name"
    :action="submitDelete"
    :error="remove.error.value?.message"
  />
</template>
