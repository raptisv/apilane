<script setup lang="ts">
import { CompassIcon } from '@lucide/vue'
import StateMessage from '@/components/StateMessage.vue'
import { Button } from '@/components/ui/button'
</script>

<template>
  <StateMessage :icon="CompassIcon" as="h1" title="Page not found" description="There is nothing at this address.">
    <Button as-child>
      <RouterLink :to="{ name: 'apps' }">Go to applications</RouterLink>
    </Button>
  </StateMessage>
</template>
