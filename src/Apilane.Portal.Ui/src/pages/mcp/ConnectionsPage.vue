<script setup lang="ts">
import { PlugIcon, UnplugIcon } from '@lucide/vue'
import { ref } from 'vue'
import ConfirmByNameDialog from '@/components/ConfirmByNameDialog.vue'
import ErrorState from '@/components/ErrorState.vue'
import LoadingState from '@/components/LoadingState.vue'
import PageHeader from '@/components/PageHeader.vue'
import StateMessage from '@/components/StateMessage.vue'
import McpConnectionsHelp from '@/components/help/McpConnectionsHelp.vue'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@/components/ui/table'
import { useAsync } from '@/composables/useAsync'
import { useMutation } from '@/composables/useMutation'
import { formatDateTime, formatUtc } from '@/lib/format'
import { loadMcpConnections, mcpConnectionStatus, revokeMcpConnection } from '@/lib/mcp'
import type { McpConnection } from '@/lib/mcp'
import { useSession } from '@/lib/session'
import * as toast from '@/lib/toast'
import McpSetupDialog from './McpSetupDialog.vue'

const session = useSession()
const { data, error, loading, reload } = useAsync(loadMcpConnections)
const setupOpen = ref(false)
const revokeOpen = ref(false)
const revoking = ref<McpConnection>()
const revoke = useMutation(revokeMcpConnection)

function openRevoke(connection: McpConnection): void {
  revoking.value = connection
  revoke.reset()
  revokeOpen.value = true
}

async function submitRevoke(): Promise<boolean> {
  if (!revoking.value || !(await revoke.run(revoking.value.Id))) {
    return false
  }
  toast.success('Connection revoked.')
  void reload()
  return true
}
</script>

<template>
  <PageHeader title="MCP connections" :description="session.IsAdmin ? 'Review and revoke the client connections on this instance.' : 'Review and revoke the client connections you approved.'">
    <McpConnectionsHelp />
    <Button @click="setupOpen = true"><PlugIcon /> MCP setup</Button>
  </PageHeader>
  <LoadingState v-if="loading" label="Loading MCP connections" />
  <ErrorState v-else-if="error" :error="error" @retry="reload" />
  <StateMessage
    v-else-if="!data?.length"
    :icon="PlugIcon"
    title="No MCP connections yet"
    description="Add the Portal's MCP address to your client and authenticate it. In the browser, select an existing agent and approve access to the listed applications you own."
  />
  <div v-else class="overflow-hidden rounded-lg border bg-card">
    <Table>
      <TableHeader>
        <TableRow>
          <TableHead class="pl-4">Connection</TableHead>
          <TableHead>Access</TableHead>
          <TableHead class="hidden lg:table-cell">Activity</TableHead>
          <TableHead class="pr-4"><span class="sr-only">Actions</span></TableHead>
        </TableRow>
      </TableHeader>
      <TableBody>
        <TableRow v-for="connection in data" :key="connection.Id">
          <TableCell class="pl-4 whitespace-normal">
            <p class="font-medium wrap-anywhere">{{ connection.Name }}</p>
            <p class="mt-0.5 text-xs text-muted-foreground wrap-anywhere">{{ connection.ClientName }}</p>
            <p v-if="session.IsAdmin" class="mt-0.5 text-xs text-muted-foreground wrap-anywhere">Approved by {{ connection.AuthorizedByEmail }}</p>
            <Badge class="mt-2" :variant="mcpConnectionStatus(connection) === 'Active' ? 'secondary' : 'outline'">{{ mcpConnectionStatus(connection) }}</Badge>
            <p class="mt-1 text-xs text-muted-foreground">
              {{ connection.RevokedAt ? 'Revoked' : 'Expires' }}
              <time :datetime="connection.RevokedAt ?? connection.ExpiresAt" :title="`${formatUtc(connection.RevokedAt ?? connection.ExpiresAt)} UTC`">{{ formatDateTime(connection.RevokedAt ?? connection.ExpiresAt) }}</time>
            </p>
            <p class="mt-1 text-xs text-muted-foreground lg:hidden">
              Last used
              <time v-if="connection.LastUsedAt" :datetime="connection.LastUsedAt" :title="`${formatUtc(connection.LastUsedAt)} UTC`">{{ formatDateTime(connection.LastUsedAt) }}</time>
              <span v-else>Never</span>
            </p>
          </TableCell>
          <TableCell class="align-top whitespace-normal wrap-anywhere">
            <p>{{ connection.AgentName }}</p>
            <ul v-if="connection.Applications.length" class="mt-1 space-y-1 text-xs text-muted-foreground">
              <li v-for="application in connection.Applications" :key="application.Token">{{ application.Name }}</li>
            </ul>
            <p v-else class="mt-1 text-xs text-muted-foreground">No applications currently available</p>
          </TableCell>
          <TableCell class="hidden align-top text-xs text-muted-foreground lg:table-cell">
            <p>Approved <time :datetime="connection.CreatedAt" :title="`${formatUtc(connection.CreatedAt)} UTC`">{{ formatDateTime(connection.CreatedAt) }}</time></p>
            <p class="mt-1">
              Last used
              <time v-if="connection.LastUsedAt" :datetime="connection.LastUsedAt" :title="`${formatUtc(connection.LastUsedAt)} UTC`">{{ formatDateTime(connection.LastUsedAt) }}</time>
              <span v-else>Never</span>
            </p>
          </TableCell>
          <TableCell class="w-px align-top pr-4">
            <Button v-if="['Active', 'Unavailable'].includes(mcpConnectionStatus(connection))" variant="ghost" size="icon-sm" title="Revoke" @click="openRevoke(connection)">
              <UnplugIcon /><span class="sr-only">Revoke connection {{ connection.Name }}</span>
            </Button>
          </TableCell>
        </TableRow>
      </TableBody>
    </Table>
  </div>

  <McpSetupDialog v-model:open="setupOpen" />
  <ConfirmByNameDialog
    v-if="revoking"
    v-model:open="revokeOpen"
    :title="`Revoke ${revoking.Name}?`"
    description="This client will lose access. The agent and its other connections will keep working. To reconnect, approve a new request from the client."
    :name="revoking.Name"
    confirm-label="Revoke connection"
    :action="submitRevoke"
    :error="revoke.error.value?.message"
  />
</template>
