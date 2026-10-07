<script setup lang="ts">
import HelpItem from '@/components/HelpItem.vue'
import HelpSheet from '@/components/HelpSheet.vue'
</script>

<template>
  <HelpSheet title="MCP connections" lead="Connections let MCP clients act as an existing Portal agent without copying its key.">
    <HelpItem title="Connect a client">
      <p>Use MCP setup to copy the Portal address and a suggested server name. Add a remote Streamable HTTP server in your client, enable OAuth or browser authentication, then review its browser request.</p>
      <p>Choose an existing agent that has access to an application you own. The approval screen lists the applications this connection can use, with the agent's current permissions. This also applies to administrators.</p>
      <p>The connection cannot access the agent's other applications or gain applications you share later. Access also ends when you no longer own an application or it is no longer shared with that agent.</p>
    </HelpItem>
    <HelpItem title="Keep projects separate">
      <p>Use a different server name and connection for each project. Give the connection a name you recognize when approving it.</p>
      <p>A project folder or connection name does not limit access. Use separate agents and application permissions when projects need different access.</p>
    </HelpItem>
    <HelpItem title="Clients in containers or on another machine">
      <p>The browser must be able to reach the client's local OAuth callback. Follow your client's instructions to forward its callback port to the browser's machine before signing in.</p>
      <p>Separate simultaneous sign-ins may need different callback ports.</p>
    </HelpItem>
    <HelpItem title="Review and revoke">
      <p>You see connections you approved. Administrators can review and revoke every connection. Last used shows when the client last used its connection.</p>
      <p>Unavailable means the connection can no longer be used, for example after its agent or application access was removed. You can still revoke it.</p>
      <p>Revoke stops the connection without deleting the agent or its other connections. To connect again, start authentication in the client and approve a new request.</p>
    </HelpItem>
  </HelpSheet>
</template>
