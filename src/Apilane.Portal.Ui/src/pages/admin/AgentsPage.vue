<script setup lang="ts">
import { BotIcon, PlusIcon, Trash2Icon, TriangleAlertIcon } from '@lucide/vue'
import { computed, ref, shallowRef } from 'vue'
import ConfirmByNameDialog from '@/components/ConfirmByNameDialog.vue'
import CopyField from '@/components/CopyField.vue'
import ErrorState from '@/components/ErrorState.vue'
import FormDialog from '@/components/FormDialog.vue'
import FormField from '@/components/FormField.vue'
import LoadingState from '@/components/LoadingState.vue'
import PageHeader from '@/components/PageHeader.vue'
import StateMessage from '@/components/StateMessage.vue'
import AgentsHelp from '@/components/help/AgentsHelp.vue'
import { Button } from '@/components/ui/button'
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from '@/components/ui/dialog'
import { Input } from '@/components/ui/input'
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@/components/ui/table'
import { useAsync } from '@/composables/useAsync'
import { useMutation } from '@/composables/useMutation'
import { accountDisplayName, agentNameRule, isAgent, isAgentName } from '@/lib/agents'
import { api, unwrap } from '@/lib/api'
import type { Schemas } from '@/lib/api'
import { formatDateTime, formatUtc } from '@/lib/format'
import { formErrors } from '@/lib/forms'
import * as toast from '@/lib/toast'

type User = Schemas['UserResponse']

// An agent never signs in: LastLogin holds the date it was added.
const { data, error, loading, reload } = useAsync(() => unwrap(api.GET('/api/v1/admin/users')))
const agents = computed(() => data.value?.Data.filter((user) => isAgent(user.Email)) ?? [])

// Add agent. An agent is a user at @agent.local (lib/agents.ts) that calls the API with a key.
const agentOpen = ref(false)
const agentName = ref('')
const nameRefused = ref(false)

// The answer of the create call carries the key, and nothing else ever does. It is held here
// only while the dialog that shows it is open.
const created = shallowRef<Schemas['AgentCreatedResponse']>()

const create = useMutation(async () => {
  created.value = await unwrap(api.POST('/api/v1/admin/agents', { body: { Name: agentName.value } }))
})
const agentErrors = computed(() => formErrors(create.error.value, ['Name']))

function openAgent(): void {
  agentName.value = ''
  nameRefused.value = false
  create.reset()
  agentOpen.value = true
}

async function submitAgent(): Promise<boolean> {
  // The API's rule, checked here first so a slip costs no round trip.
  nameRefused.value = !isAgentName(agentName.value)

  if (nameRefused.value || !(await create.run())) {
    return false
  }

  void reload()
  return true
}

function closeKey(): void {
  created.value = undefined
}

// Delete agent. `deleting` keeps its value after the dialog closes, so the text does not change while it fades out.
const deleteOpen = ref(false)
const deleting = ref<User>()

const remove = useMutation((userId: string) =>
  unwrap(api.DELETE('/api/v1/admin/agents/{userId}', { params: { path: { userId } } })),
)

function openDelete(user: User): void {
  deleting.value = user
  remove.reset()
  deleteOpen.value = true
}

async function submitDelete(): Promise<boolean> {
  if (!deleting.value || !(await remove.run(deleting.value.ID))) {
    return false
  }

  toast.success('Agent deleted.')
  void reload()
  return true
}
</script>

<template>
  <PageHeader title="Agents" description="Accounts for scripts and AI agents that manage shared applications with a key.">
    <AgentsHelp />
    <Button @click="openAgent">
      <PlusIcon />
      Add agent
    </Button>
  </PageHeader>

  <LoadingState v-if="loading" label="Loading agents" />

  <ErrorState v-else-if="error" :error="error" @retry="reload" />

  <StateMessage
    v-else-if="agents.length === 0"
    :icon="BotIcon"
    title="No agents yet"
    description="Add an agent, then select it in an application's Sharing tab and choose its permissions."
  >
    <Button @click="openAgent">
      <PlusIcon />
      Add agent
    </Button>
  </StateMessage>

  <div v-else class="overflow-hidden rounded-lg border bg-card">
    <Table>
      <TableHeader>
        <TableRow>
          <TableHead class="pl-4">Agent</TableHead>
          <TableHead class="hidden md:table-cell">Added</TableHead>
          <TableHead class="pr-4"><span class="sr-only">Actions</span></TableHead>
        </TableRow>
      </TableHeader>
      <TableBody>
        <TableRow v-for="agent in agents" :key="agent.ID">
          <TableCell class="pl-4 whitespace-normal">
            <p class="font-medium wrap-anywhere">{{ accountDisplayName(agent.Email) }}</p>
            <p class="mt-0.5 font-mono text-xs break-all text-muted-foreground">{{ agent.ID }}</p>
            <!-- On a phone the added date moves under the name, so the table fits the screen. -->
            <p class="mt-0.5 text-xs text-muted-foreground md:hidden">
              Added
              <time :datetime="agent.LastLogin" :title="`${formatUtc(agent.LastLogin)} UTC`">
                {{ formatDateTime(agent.LastLogin) }}
              </time>
            </p>
          </TableCell>
          <TableCell class="hidden text-muted-foreground md:table-cell">
            <time :datetime="agent.LastLogin" :title="`${formatUtc(agent.LastLogin)} UTC`">
              {{ formatDateTime(agent.LastLogin) }}
            </time>
          </TableCell>
          <TableCell class="w-px pr-4">
            <div class="flex justify-end">
              <Button variant="ghost" size="icon-sm" title="Delete" @click="openDelete(agent)">
                <Trash2Icon />
                <span class="sr-only">Delete agent {{ accountDisplayName(agent.Email) }}</span>
              </Button>
            </div>
          </TableCell>
        </TableRow>
      </TableBody>
    </Table>
  </div>

  <FormDialog
    v-model:open="agentOpen"
    title="Add agent"
    description="Creates an account without a password and a key for it. The key is shown once, right after this."
    submit-label="Add agent"
    :submit="submitAgent"
    :error="agentErrors.message"
  >
    <FormField
      v-slot="{ field }"
      label="Name"
      :help="agentNameRule"
      :error="nameRefused ? agentNameRule : agentErrors.fields.Name"
    >
      <Input v-model="agentName" v-bind="field" autocomplete="off" autocapitalize="off" spellcheck="false" />
    </FormField>
  </FormDialog>

  <!-- The key of the agent that was just added. A click outside does not close it: the key cannot be shown again. -->
  <Dialog :open="created !== undefined" @update:open="(open: boolean) => !open && closeKey()">
    <DialogContent
      class="max-h-[calc(100dvh-2rem)] overflow-y-auto sm:max-w-lg"
      @interact-outside="(event: Event) => event.preventDefault()"
    >
      <DialogHeader class="pr-8">
        <DialogTitle>Agent added</DialogTitle>
        <DialogDescription>Copy the key now and give it to the script or AI agent.</DialogDescription>
      </DialogHeader>

      <CopyField label="Name" :value="accountDisplayName(created?.Email ?? '')" />
      <CopyField label="Key" :value="created?.Key ?? ''" />

      <div class="flex gap-2.5 rounded-lg border border-warning/40 bg-warning/10 p-3 text-sm text-warning">
        <TriangleAlertIcon class="mt-0.5 size-4 shrink-0" aria-hidden="true" />
        <p>
          The key is shown only now. The Portal keeps a hash of it and cannot show it again: if it is lost, delete the
          agent and add it again.
        </p>
      </div>

      <DialogFooter>
        <Button type="button" @click="closeKey">Done</Button>
      </DialogFooter>
    </DialogContent>
  </Dialog>

  <ConfirmByNameDialog
    v-if="deleting"
    v-model:open="deleteOpen"
    :title="`Delete agent ${accountDisplayName(deleting.Email)}?`"
    description="Its key stops working at once, and it is removed from every application shared with it. This cannot be undone."
    :name="accountDisplayName(deleting.Email)"
    :action="submitDelete"
    :error="remove.error.value?.message"
  />
</template>
