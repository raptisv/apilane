<script setup lang="ts">
import { computed } from 'vue'
import { Badge } from '@/components/ui/badge'
import { describeAccess, rateLimitShort, roleName } from '@/lib/securityAccess'
import type { Access, SecurityRole } from '@/lib/securityAccess'

// One allowed action in the tree and the matrix of the security screen: green for full access,
// amber when restricted (owned records, some or no properties), dashed when inherited from
// Anonymous or Authenticated. The rate limit shows next to the action.
// The details are in its title and, for screen readers, in its text.
const props = defineProps<{
  access: Access
  roles: SecurityRole[]
}>()

const details = computed(() => {
  const lines = describeAccess(props.access)

  if (props.access.inherited) {
    lines.push(`Inherited from ${props.access.from.map((id) => roleName(props.roles, id)).join(' and ')}`)
  }

  return lines
})
</script>

<template>
  <Badge
    variant="outline"
    class="font-mono"
    :class="[access.full ? 'border-success/50 text-success' : 'border-warning/50 text-warning', access.inherited && 'border-dashed']"
    :title="details.join('\n')"
  >
    {{ access.action.toUpperCase() }}<template v-if="access.rateLimit"> {{ rateLimitShort(access.rateLimit) }}</template>
    <span class="sr-only">: {{ details.join('. ') }}</span>
  </Badge>
</template>
