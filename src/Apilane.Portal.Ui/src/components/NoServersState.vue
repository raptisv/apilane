<script setup lang="ts">
import { ServerIcon } from '@lucide/vue'
import { Button } from '@/components/ui/button'
import { useSession } from '@/lib/session'
import StateMessage from './StateMessage.vue'

// Shown instead of a form that needs an API server (new, import, clone) when the instance has
// none: an application cannot exist without one. Only an administrator can add a server.
const session = useSession()
</script>

<template>
  <StateMessage
    :icon="ServerIcon"
    title="No API server yet"
    :description="
      session.IsAdmin
        ? 'An application lives on an API server. Add a server first.'
        : 'An application lives on an API server, and this instance has none yet. Ask an administrator to add one.'
    "
  >
    <Button v-if="session.IsAdmin" as-child>
      <RouterLink :to="{ name: 'admin-servers' }">Go to servers</RouterLink>
    </Button>
    <Button as-child variant="outline">
      <RouterLink :to="{ name: 'apps' }">Back to applications</RouterLink>
    </Button>
  </StateMessage>
</template>
