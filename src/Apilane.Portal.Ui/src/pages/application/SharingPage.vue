<script setup lang="ts">
import { PlusIcon, Trash2Icon, TriangleAlertIcon, UsersIcon } from '@lucide/vue'
import { computed, ref } from 'vue'
import ConfirmDialog from '@/components/ConfirmDialog.vue'
import ErrorState from '@/components/ErrorState.vue'
import FormDialog from '@/components/FormDialog.vue'
import FormField from '@/components/FormField.vue'
import LoadingState from '@/components/LoadingState.vue'
import PageHeader from '@/components/PageHeader.vue'
import StateMessage from '@/components/StateMessage.vue'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { useApplication } from '@/composables/useApplication'
import { useApplications } from '@/composables/useApplications'
import { useAsync } from '@/composables/useAsync'
import { useMutation } from '@/composables/useMutation'
import { isAgent } from '@/lib/agents'
import { api, unwrap } from '@/lib/api'
import type { Schemas } from '@/lib/api'
import { formErrors } from '@/lib/forms'
import * as toast from '@/lib/toast'

type Collaborator = Schemas['CollaboratorResponse']

// Owner only: the route carries meta.requiresOwner, so AppLayout shows the Forbidden state to a
// collaborator and this screen never loads for one.
const { application } = useApplication()
const { reload: reloadApplications } = useApplications()

// AppLayout gives every application a fresh screen, so the token is fixed for the life of this one.
const appToken = application.value.Token

const { data, error, loading, reload } = useAsync(() =>
  unwrap(api.GET('/api/v1/applications/{appToken}/collaborators', { params: { path: { appToken } } })),
)

// The agents this application is not shared with yet, offered under the address box. The box
// stays free text, so a failed read only means there is nothing to offer.
const { data: agents, reload: reloadAgents } = useAsync(() =>
  unwrap(api.GET('/api/v1/applications/{appToken}/collaborators/available-agents', { params: { path: { appToken } } })),
)

// Every change also reloads the applications list, so 'Shared with N' on the cards is right.
function afterChange(): void {
  void reload()
  void reloadAgents()
  void reloadApplications()
}

// Share
const shareOpen = ref(false)
const email = ref('')

// useMutation does not hand back the answer; the toast needs to know whether the mail went out.
let added: Schemas['CollaboratorAddedResponse'] | undefined

const share = useMutation(async () => {
  added = await unwrap(
    api.POST('/api/v1/applications/{appToken}/collaborators', { params: { path: { appToken } }, body: { Email: email.value } }),
  )
})
const shareErrors = computed(() => formErrors(share.error.value, ['Email']))

// Agents are mentioned only when there is one to offer.
const emailHelp = computed(
  () =>
    'The address the user signs in with, written exactly as in their account. The user has to be a registered user of this instance.' +
    (agents.value?.Data.length ? ' The agents you can still add are offered as you type.' : ''),
)

function openShare(): void {
  email.value = ''
  share.reset()
  shareOpen.value = true
}

async function submitShare(): Promise<boolean> {
  if (!(await share.run()) || !added) {
    return false
  }

  // An agent is never mailed, and nobody has to be told.
  // NotificationSent = false: mail is not set up, or the mail could not be prepared. The owner has to
  // tell the user, so it is a warning. True only means the mail was handed over for sending.
  if (isAgent(added.Email)) {
    toast.success(`Shared with ${added.Email}.`)
  } else if (added.NotificationSent) {
    toast.success(`Shared with ${added.Email}. A notification email was sent.`)
  } else {
    toast.warning(
      `Shared with ${added.Email}. No email was sent: mail is not set up on this instance, or the mail could not be prepared.`,
    )
  }
  afterChange()
  return true
}

// Remove. `removing` keeps its value after the dialog closes, so the text does not change while it fades out.
const removeOpen = ref(false)
const removing = ref<Collaborator>()

const remove = useMutation((id: number) =>
  unwrap(api.DELETE('/api/v1/applications/{appToken}/collaborators/{id}', { params: { path: { appToken, id } } })),
)

function openRemove(collaborator: Collaborator): void {
  removing.value = collaborator
  remove.reset()
  removeOpen.value = true
}

async function submitRemove(): Promise<boolean> {
  if (!removing.value || !(await remove.run(removing.value.ID))) {
    return false
  }

  toast.success(`Stopped sharing with ${removing.value.Email}.`)
  // Wait for the list before the dialog closes: the row and its button are gone by then, so the
  // dialog moves focus to the main content instead of to a button that is about to disappear.
  void reloadAgents()
  void reloadApplications()
  await reload()
  return true
}
</script>

<template>
  <PageHeader title="Sharing" description="The users who manage this application with you.">
    <Button @click="openShare">
      <PlusIcon />
      Share
    </Button>
  </PageHeader>

  <div class="grid max-w-2xl gap-6">
    <LoadingState v-if="loading" label="Loading the users this application is shared with" />

    <ErrorState v-else-if="error" :error="error" @retry="reload" />

    <StateMessage
      v-else-if="!data || data.Data.length === 0"
      :icon="UsersIcon"
      title="Not shared yet"
      description="Share it to let another user of this instance manage it with you."
    >
      <Button @click="openShare">
        <PlusIcon />
        Share with another user
      </Button>
    </StateMessage>

    <section v-else aria-labelledby="shared-title" class="rounded-lg border bg-card">
      <h2 id="shared-title" class="border-b px-4 py-2.5 text-sm font-semibold">Shared with</h2>
      <ul class="divide-y">
        <li v-for="collaborator in data.Data" :key="collaborator.ID" class="flex items-center gap-3 py-2 pr-2 pl-4">
          <a
            :href="`mailto:${collaborator.Email}`"
            class="min-w-0 flex-1 rounded-sm text-sm wrap-anywhere underline-offset-4 hover:underline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring"
          >
            {{ collaborator.Email }}
            <span class="sr-only">(write an email)</span>
          </a>
          <Button variant="ghost" size="icon-sm" title="Stop sharing" @click="openRemove(collaborator)">
            <Trash2Icon />
            <span class="sr-only">Stop sharing with {{ collaborator.Email }}</span>
          </Button>
        </li>
      </ul>
    </section>

    <section aria-labelledby="about-title" class="grid gap-2 rounded-lg border bg-card p-4 text-sm">
      <h2 id="about-title" class="font-semibold">What is an application collaborator?</h2>
      <p class="text-muted-foreground">
        When you add a user as an application collaborator, you give that user administrator access to that application.
      </p>
      <ul class="grid list-disc gap-1 pl-5 text-muted-foreground">
        <li class="font-semibold text-foreground">The user has full access to modify the application, even delete it.</li>
        <li>The user has to be a registered user of this instance.</li>
        <li>The collaborator will not be able to add other collaborators on that application.</li>
        <li>You can remove any collaborator at any time.</li>
      </ul>
    </section>
  </div>

  <FormDialog
    v-model:open="shareOpen"
    title="Share with another user"
    submit-label="Share"
    :submit="submitShare"
    :error="shareErrors.message"
  >
    <FormField
      v-slot="{ field }"
      label="User email"
      :help="emailHelp"
      :error="shareErrors.fields.Email"
    >
      <!-- Free text, with the available agents as suggestions of the browser's own list. -->
      <Input v-model="email" v-bind="field" type="email" list="available-agents" autocomplete="off" spellcheck="false" />
      <datalist id="available-agents">
        <option v-for="agent in agents?.Data" :key="agent.Email" :value="agent.Email" />
      </datalist>
    </FormField>

    <div class="flex gap-2.5 rounded-lg border border-warning/40 bg-warning/10 p-3 text-sm text-warning">
      <TriangleAlertIcon class="mt-0.5 size-4 shrink-0" aria-hidden="true" />
      <p>
        The user gets full access to modify the application, even delete it. When mail is set up on this instance, the
        user is notified by email. An agent is not notified, and cannot delete or rebuild the application.
      </p>
    </div>
  </FormDialog>

  <ConfirmDialog
    v-if="removing"
    v-model:open="removeOpen"
    :title="`Stop sharing with ${removing.Email}?`"
    :description="`User ${removing.Email} will lose administrator access to this application. You can share the application with the user again at any time.`"
    confirm-label="Stop sharing"
    destructive
    :action="submitRemove"
    :error="remove.error.value?.message"
  />
</template>
