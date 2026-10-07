<script setup lang="ts">
import { BotIcon, CircleAlertIcon, Loader2Icon, ShieldCheckIcon } from '@lucide/vue'
import { computed, ref, watch } from 'vue'
import { useRoute } from 'vue-router'
import ErrorState from '@/components/ErrorState.vue'
import FormField from '@/components/FormField.vue'
import LoadingState from '@/components/LoadingState.vue'
import PageHeader from '@/components/PageHeader.vue'
import StateMessage from '@/components/StateMessage.vue'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'
import { useAsync } from '@/composables/useAsync'
import { useMutation } from '@/composables/useMutation'
import { ApiError } from '@/lib/api'
import { formErrors } from '@/lib/forms'
import { approveMcpAuthorization, denyMcpAuthorization, loadMcpAuthorization, returnToMcpClient } from '@/lib/mcp'

const route = useRoute()
const requestId = computed(() => typeof route.query.requestId === 'string' ? route.query.requestId : '')
const { data, error, loading, reload } = useAsync(async () => {
  if (!requestId.value) {
    throw new ApiError(404, { Code: 'NOT_FOUND', Message: 'This connection request is not valid.' })
  }
  return loadMcpAuthorization(requestId.value)
}, { watch: requestId })
const redirectHost = computed(() => data.value ? new URL(data.value.RedirectUri).host : '')

const agentId = ref('')
const connectionName = ref('')
const agentError = ref<string>()
const nameError = ref<string>()
const returning = ref(false)
const decisionKind = ref<'approve' | 'deny'>()
const selectedAgent = computed(() => data.value?.Agents.find((agent) => agent.Id === agentId.value))

const decision = useMutation(async (allow: boolean) => {
  const request = data.value
  if (!request) {
    return
  }
  decisionKind.value = allow ? 'approve' : 'deny'
  const result = allow
    ? await approveMcpAuthorization({
      RequestId: request.RequestId,
      AgentId: agentId.value,
      Name: connectionName.value.trim(),
      ApplicationTokens: selectedAgent.value?.Applications.map((application) => application.Token) ?? [],
    })
    : await denyMcpAuthorization(request.RequestId)
  returnToMcpClient(result.RedirectUrl, request.RedirectUri)
  returning.value = true
})
const errors = computed(() => formErrors(decision.error.value, ['AgentId', 'Name']))
const busy = computed(() => decision.pending.value || returning.value)
const unavailable = computed(() => [error.value, decision.error.value].some((failure) => failure instanceof ApiError && failure.status === 404))

watch(data, (request) => {
  agentId.value = ''
  connectionName.value = request?.ClientName.slice(0, 100) ?? ''
  agentError.value = undefined
  nameError.value = undefined
  decision.reset()
})
watch(agentId, () => (agentError.value = undefined))
watch(connectionName, () => (nameError.value = undefined))

async function approve(): Promise<void> {
  if (busy.value || !data.value) {
    return
  }
  agentError.value = selectedAgent.value ? undefined : 'Select an existing agent.'
  nameError.value = connectionName.value.trim().length > 0 && connectionName.value.trim().length <= 100
    ? undefined : 'Enter a connection name with 1 to 100 characters.'
  if (!agentError.value && !nameError.value) {
    await decision.run(true)
  }
}

async function deny(): Promise<void> {
  if (!busy.value && data.value) {
    await decision.run(false)
  }
}
</script>

<template>
  <div class="mx-auto max-w-2xl">
    <PageHeader title="Connect an MCP client" description="Review this request before giving a client access to an agent." />
    <LoadingState v-if="loading" label="Loading connection request" />
    <StateMessage
      v-else-if="unavailable"
      :icon="CircleAlertIcon"
      title="This connection request is no longer available"
      description="It may have expired or already been used. Start connecting again in your client."
    />
    <ErrorState v-else-if="error" :error="error" @retry="reload" />
    <form v-else-if="data" class="space-y-6 rounded-lg border bg-card p-5 sm:p-6" @submit.prevent="approve">
      <dl class="grid gap-4 text-sm">
        <div>
          <dt class="text-muted-foreground">Requesting client</dt>
          <dd class="mt-1 wrap-anywhere font-medium">{{ data.ClientName }}</dd>
        </div>
        <div>
          <dt class="text-muted-foreground">Return address</dt>
          <dd class="mt-1 wrap-anywhere font-mono">{{ redirectHost }}</dd>
        </div>
      </dl>
      <p class="text-sm text-muted-foreground">Approve only if you started this connection and recognize the client and return address.</p>

      <p v-if="errors.message" role="alert" class="text-sm text-destructive">{{ errors.message }}</p>
      <StateMessage
        v-if="data.Agents.length === 0"
        :icon="BotIcon"
        title="No agents available"
        description="Share an existing agent with an application you own, then start a new connection request. Only agents with access to your own applications can be connected."
      />
      <template v-else>
        <FormField v-slot="{ field }" label="Agent" :error="agentError ?? errors.fields.AgentId" help="Choose the existing agent this connection will act as.">
          <Select v-model="agentId" :disabled="busy">
            <SelectTrigger v-bind="field"><SelectValue placeholder="Select an agent" /></SelectTrigger>
            <SelectContent>
              <SelectItem v-for="agent in data.Agents" :key="agent.Id" :value="agent.Id">{{ agent.Name }}</SelectItem>
            </SelectContent>
          </Select>
        </FormField>
        <FormField v-slot="{ field }" label="Connection name" :error="nameError ?? errors.fields.Name" help="A name to recognize later, such as your project folder. It does not restrict access to that folder.">
          <Input v-model="connectionName" v-bind="field" :disabled="busy" maxlength="100" autocomplete="off" />
        </FormField>
        <div class="rounded-md border bg-muted/40 p-4 text-sm">
          <p class="flex items-center gap-2 font-medium"><ShieldCheckIcon class="size-4 shrink-0" /> What this allows</p>
          <template v-if="selectedAgent">
            <p class="mt-2">This client acts as {{ selectedAgent.Name }} in these applications you own:</p>
            <ul class="mt-2 list-disc space-y-1 pl-5">
              <li v-for="application in selectedAgent.Applications" :key="application.Token" class="wrap-anywhere">{{ application.Name }}</li>
            </ul>
            <p class="mt-2">It uses the agent's current permissions within these applications. The agent's other applications and future shares are not included.</p>
          </template>
          <p v-else class="mt-2">Select an agent to see which of your applications this connection can access.</p>
          <p class="mt-2 text-muted-foreground">The connection lasts up to 30 days. You can revoke it at any time in MCP connections, keeping the agent and its other connections.</p>
        </div>
      </template>

      <p v-if="returning" role="status" class="text-sm text-muted-foreground">Returning to {{ data.ClientName }}…</p>
      <div class="flex flex-wrap justify-end gap-2 border-t pt-4">
        <Button type="button" variant="outline" :disabled="busy" @click="deny">
          <Loader2Icon v-if="decision.pending.value && decisionKind === 'deny'" class="animate-spin" />
          Deny
        </Button>
        <Button type="submit" :disabled="busy || !selectedAgent">
          <Loader2Icon v-if="decision.pending.value && decisionKind === 'approve'" class="animate-spin" />
          Approve connection
        </Button>
      </div>
    </form>
  </div>
</template>
