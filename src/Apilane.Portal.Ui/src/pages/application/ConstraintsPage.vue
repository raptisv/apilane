<script setup lang="ts">
import { CheckCheckIcon, CircleAlertIcon, LinkIcon, LockIcon, PlusIcon, ShieldCheckIcon, Undo2Icon, XIcon } from '@lucide/vue'
import { computed, nextTick, ref } from 'vue'
import ErrorState from '@/components/ErrorState.vue'
import LoadingState from '@/components/LoadingState.vue'
import StateMessage from '@/components/StateMessage.vue'
import UnsavedChangesBar from '@/components/UnsavedChangesBar.vue'
import { Alert, AlertDescription } from '@/components/ui/alert'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { useApplication } from '@/composables/useApplication'
import { useAsync } from '@/composables/useAsync'
import { useEntity } from '@/composables/useEntity'
import { useMutation } from '@/composables/useMutation'
import { api, unwrap } from '@/lib/api'
import { addConstraint, changesText, constraintRequests, constraintRows, undoRemoval } from '@/lib/constraints'
import type { ConstraintEdits, ConstraintRow } from '@/lib/constraints'
import { constraintDescription, constraintLabel, constraintTypeText, onDeleteText } from '@/lib/entities'
import type { Constraint } from '@/lib/entities'
import { listErrors } from '@/lib/forms'
import { useSession } from '@/lib/session'
import * as toast from '@/lib/toast'
import AddConstraintDialog from './AddConstraintDialog.vue'

// The constraints of one entity. Additions and removals collect in the list and are sent together
// with one Save, which replaces every custom constraint. System constraints stay as they are.
const { application } = useApplication()
const { entity } = useEntity()
const session = useSession()

// AppLayout and EntityLayout give every application and entity a fresh screen.
const path = { params: { path: { appToken: application.value.Token, entity: entity.value.Name } } }

const { data, error, loading, reload } = useAsync(() =>
  unwrap(api.GET('/api/v1/applications/{appToken}/entities/{entity}/constraints', path)),
)

// Only an administrator may change the constraints of a system entity; everyone else reads them.
const editable = computed(() => !entity.value.IsSystem || session.value.IsAdmin)

const edits = ref<ConstraintEdits>({ removed: [], added: [] })
const rows = computed(() => constraintRows(data.value?.Constraints ?? [], edits.value.removed, edits.value.added))
const dirty = computed(() => edits.value.removed.length > 0 || edits.value.added.length > 0)

const save = useMutation(async () => {
  data.value = await unwrap(
    api.PUT('/api/v1/applications/{appToken}/entities/{entity}/constraints', {
      ...path,
      body: { Constraints: constraintRequests(rows.value) },
    }),
  )
})

// The API names each problem by the place of the constraint in the request (requestIndex).
const errors = computed(() => listErrors(save.error.value, 'Constraints'))
const topMessage = computed(
  () => errors.value.message ?? (Object.keys(errors.value.rows).length > 0 ? 'Fix the constraints marked below, then save again.' : undefined),
)

function rowError(row: ConstraintRow): string | undefined {
  return row.requestIndex === undefined ? undefined : errors.value.rows[row.requestIndex]
}

// Any edit makes the messages of the last save stale: the places in the list have moved.
function edit(next: ConstraintEdits): void {
  edits.value = next
  save.reset()
}

function add(constraint: Constraint): void {
  edit(addConstraint(data.value?.Constraints ?? [], edits.value, constraint))
}

function remove(row: ConstraintRow, index: number): void {
  if (row.status === 'added') {
    edit({ ...edits.value, added: edits.value.added.filter((item) => item !== row.constraint) })
  } else {
    edit({ ...edits.value, removed: [...edits.value.removed, row.constraint] })
  }

  void focusAfter(index)
}

function undoRemove(row: ConstraintRow, index: number): void {
  edit(undoRemoval(edits.value, row.constraint))
  void focusAfter(index)
}

// Remove and Undo replace the pressed button, or the whole row: keep the focus on the button now
// in that place (rows are keyed by place), or on 'Add constraint' when there is none.
async function focusAfter(index: number): Promise<void> {
  await nextTick()
  const next =
    document.querySelector<HTMLElement>(`[data-row-action="${index}"]`) ?? document.querySelector<HTMLElement>('[data-add-constraint]')
  next?.focus()
}

function discard(): void {
  edit({ removed: [], added: [] })
}

async function submit(): Promise<void> {
  if (!(await save.run())) {
    return
  }

  edits.value = { removed: [], added: [] }
  toast.success('Constraints saved.')
}

const addOpen = ref(false)

const rowClass: Record<ConstraintRow['status'], string> = {
  saved: '',
  added: 'border-success/50 bg-success/5',
  removed: 'border-dashed bg-muted/30',
}

// Badges wrap their text, so a long name does not widen the page on a phone.
const badgeClass = 'h-auto max-w-full justify-start whitespace-normal'
</script>

<template>
  <div class="max-w-3xl">
    <div class="mb-4 flex flex-wrap items-start justify-between gap-x-6 gap-y-3">
      <div class="min-w-0 space-y-1.5 text-sm text-muted-foreground">
        <h2 class="text-sm font-semibold text-foreground">Constraints</h2>
        <p>
          <span class="font-medium text-foreground">Unique:</span>
          no two records may have the same value in a property, or the same combination of values in several.
        </p>
        <p>
          <span class="font-medium text-foreground">Foreign key:</span>
          a property holds the ID of a record of another entity, and that record must exist.
        </p>
        <p>Changes are applied to the table when you save. Saving fails when existing records break a new constraint.</p>
      </div>
      <!-- Locked while saving: an edit made now would be lost when the save resets the list. -->
      <Button
        v-if="editable && data && rows.length > 0"
        size="sm"
        data-add-constraint
        :disabled="save.pending.value"
        @click="addOpen = true"
      >
        <PlusIcon />
        Add constraint
      </Button>
    </div>

    <LoadingState v-if="loading" label="Loading the constraints" />

    <ErrorState v-else-if="error" :error="error" @retry="reload" />

    <template v-else-if="data">
      <div v-if="!editable" class="mb-4 flex gap-2.5 rounded-lg border bg-muted/30 p-3 text-sm text-muted-foreground">
        <LockIcon class="mt-0.5 size-4 shrink-0" aria-hidden="true" />
        <p>Only an administrator can change the constraints of a system entity.</p>
      </div>

      <Alert v-if="topMessage" variant="destructive" role="alert" class="mb-4">
        <CircleAlertIcon />
        <AlertDescription>{{ topMessage }}</AlertDescription>
      </Alert>

      <StateMessage
        v-if="rows.length === 0"
        :icon="ShieldCheckIcon"
        title="No constraints yet"
        description="Records of this entity are checked only against the rules of their properties."
      >
        <Button v-if="editable" data-add-constraint @click="addOpen = true">
          <PlusIcon />
          Add constraint
        </Button>
      </StateMessage>

      <ul v-else class="grid gap-2" aria-label="Constraints">
        <li
          v-for="(row, index) in rows"
          :key="index"
          class="rounded-lg border px-3 py-2.5"
          :class="rowError(row) ? 'border-destructive/60' : rowClass[row.status]"
        >
          <div class="flex items-start justify-between gap-3">
            <div class="flex min-w-0 flex-wrap items-center gap-1.5" :class="row.status === 'removed' && 'opacity-60'">
              <span class="sr-only">{{ constraintDescription(row.constraint) }}.</span>

              <Badge
                variant="outline"
                aria-hidden="true"
                :class="row.constraint.Type === 'ForeignKey' ? 'border-warning/40 text-warning' : 'border-link/40 text-link'"
              >
                <component :is="row.constraint.Type === 'ForeignKey' ? LinkIcon : CheckCheckIcon" />
                {{ constraintTypeText(row.constraint.Type) }}
              </Badge>

              <template v-if="row.constraint.Type === 'ForeignKey'">
                <Badge variant="secondary" aria-hidden="true" class="font-mono" :class="badgeClass">
                  <span class="wrap-anywhere">{{ constraintLabel(row.constraint) }}</span>
                </Badge>
                <Badge variant="outline" aria-hidden="true">On delete {{ onDeleteText(row.constraint.OnDelete) }}</Badge>
              </template>
              <template v-else>
                <Badge
                  v-for="property in row.constraint.Properties"
                  :key="property"
                  variant="secondary"
                  aria-hidden="true"
                  class="font-mono"
                  :class="badgeClass"
                >
                  <span class="wrap-anywhere">{{ property }}</span>
                </Badge>
              </template>

              <Badge v-if="row.constraint.IsSystem" variant="outline" class="text-muted-foreground">
                <LockIcon aria-hidden="true" />
                System
              </Badge>
              <Badge v-if="row.status === 'added'" variant="outline" class="border-success/50 text-success">New, not saved</Badge>
              <Badge v-if="row.status === 'removed'" variant="destructive">Removed on save</Badge>
            </div>

            <template v-if="editable && !row.constraint.IsSystem">
              <Button
                v-if="row.status === 'removed'"
                variant="ghost"
                size="sm"
                class="shrink-0"
                :data-row-action="index"
                :disabled="save.pending.value"
                @click="undoRemove(row, index)"
              >
                <Undo2Icon />
                Undo
                <span class="sr-only">removing {{ constraintDescription(row.constraint) }}</span>
              </Button>
              <Button
                v-else
                variant="ghost"
                size="icon-sm"
                class="shrink-0"
                title="Remove"
                :data-row-action="index"
                :disabled="save.pending.value"
                @click="remove(row, index)"
              >
                <XIcon />
                <span class="sr-only">Remove {{ constraintDescription(row.constraint) }}</span>
              </Button>
            </template>
          </div>

          <p v-if="row.constraint.IsSystem" class="mt-1.5 text-xs text-muted-foreground">
            The entity comes with it. It cannot be removed.
          </p>
          <p v-if="rowError(row)" role="alert" class="mt-1.5 text-xs text-destructive">{{ rowError(row) }}</p>
        </li>
      </ul>

      <AddConstraintDialog v-if="editable" v-model:open="addOpen" :candidates="data.Candidates" :rows="rows" @add="add" />
    </template>

    <UnsavedChangesBar
      :dirty="dirty"
      :pending="save.pending.value"
      :message="changesText(edits)"
      @save="submit"
      @discard="discard"
    />
  </div>
</template>
