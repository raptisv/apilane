<script setup lang="ts">
import { ref, watch } from 'vue'
import CopyField from '@/components/CopyField.vue'
import ErrorState from '@/components/ErrorState.vue'
import LoadingState from '@/components/LoadingState.vue'
import { Button } from '@/components/ui/button'
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from '@/components/ui/dialog'
import { useAsync } from '@/composables/useAsync'
import { loadMcpEndpoint } from '@/lib/mcp'

const open = defineModel<boolean>('open', { required: true })
const { data: endpoint, error, loading, reload } = useAsync(async () => open.value ? loadMcpEndpoint() : undefined, { watch: open })
const serverName = ref('')

watch(open, (value) => {
  if (value) {
    const suffix = Array.from(crypto.getRandomValues(new Uint8Array(6)), (part) => part.toString(16).padStart(2, '0')).join('')
    serverName.value = `apilane_${suffix}`
  }
}, { immediate: true })
</script>

<template>
  <Dialog v-model:open="open">
    <DialogContent class="max-h-[calc(100dvh-2rem)] overflow-y-auto sm:max-w-xl">
      <DialogHeader class="pr-6">
        <DialogTitle>MCP setup</DialogTitle>
        <DialogDescription>Connect your MCP client to this Portal, then approve its access in the browser.</DialogDescription>
      </DialogHeader>
      <LoadingState v-if="loading" label="Loading MCP address" />
      <ErrorState v-else-if="error" :error="error" @retry="reload" />
      <div v-else-if="endpoint" class="space-y-4">
        <CopyField label="MCP address" :value="endpoint" />
        <CopyField label="Server name" :value="serverName" />
        <p class="text-sm text-muted-foreground">Use a different server name and connection for each project. You can use this suggested name or choose your own in the client.</p>
        <ol class="list-decimal space-y-2 pl-5 text-sm">
          <li>In your client's MCP settings, add a remote server using this address and the Streamable HTTP transport.</li>
          <li>Enable OAuth or browser authentication and connect, following your client's instructions.</li>
          <li>In the browser, select an existing agent and approve access to the listed applications you own.</li>
        </ol>
        <p class="text-xs text-muted-foreground">
          If your client runs in a container or on another machine, its local OAuth callback must be reachable from this browser.
          Follow your client's instructions to forward the callback port before signing in.
        </p>
        <p class="text-xs text-muted-foreground">Project settings control where the tools appear. Access follows the approved applications and agent permissions, and is not restricted by the folder name. This address and server name contain no credentials.</p>
      </div>
      <DialogFooter><Button type="button" variant="outline" @click="open = false">Done</Button></DialogFooter>
    </DialogContent>
  </Dialog>
</template>
