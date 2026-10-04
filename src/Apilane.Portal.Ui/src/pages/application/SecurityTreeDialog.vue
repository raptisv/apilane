<script setup lang="ts">
import { ChevronRightIcon, ShieldOffIcon } from '@lucide/vue'
import { computed, ref } from 'vue'
import FullScreenDialog from '@/components/FullScreenDialog.vue'
import StateMessage from '@/components/StateMessage.vue'
import { Button } from '@/components/ui/button'
import { describeAccess, itemAccess, itemKey, roleName } from '@/lib/securityAccess'
import type { SecurityItem, SecurityRole, SecurityRule } from '@/lib/securityAccess'
import SecurityAccessBadge from './SecurityAccessBadge.vue'

// The saved rules as a tree, read-only: item, then the roles that have access, then each action
// with its records, properties and rate limit. Roles include what they inherit.
const props = defineProps<{
  items: SecurityItem[]
  roles: SecurityRole[]
  rules: SecurityRule[]
  /** The editor holds changes that are not saved (and so not shown here). */
  dirty: boolean
}>()

const open = defineModel<boolean>('open', { required: true })

const tree = computed(() =>
  props.items
    .map((item) => ({
      item,
      roles: props.roles
        .map((role) => ({ role, access: itemAccess(props.rules, item, role.RoleID).filter((access) => access.allowed) }))
        .filter((node) => node.access.length > 0),
    }))
    .filter((node) => node.roles.length > 0),
)

// Items start open and roles closed. Expand all and Collapse all draw the tree again, open or closed.
const depth = ref<'items' | 'all' | 'none'>('items')
const drawn = ref(0)

function expand(next: 'all' | 'none'): void {
  depth.value = next
  drawn.value++
}

const typeNames: Record<string, string> = { Schema: 'Schema', Entity: 'Entity', CustomEndpoint: 'Custom endpoint' }
const summaryClass =
  'flex cursor-pointer list-none items-center gap-2 rounded-md px-2 py-1.5 select-none hover:bg-muted focus-visible:outline-2 focus-visible:outline-ring [&::-webkit-details-marker]:hidden'
</script>

<template>
  <FullScreenDialog
    v-model:open="open"
    title="Security tree"
    description="Who may do what, item by item, as the API server applies the saved rules. A role includes what it inherits from Anonymous and Authenticated."
  >
    <template #header>
      <p v-if="dirty" class="text-xs text-warning">Shows the saved rules: your unsaved changes are not included.</p>
    </template>

    <StateMessage v-if="tree.length === 0" :icon="ShieldOffIcon" title="No security rules configured" />

    <template v-else>
      <div class="mb-3 flex gap-2">
        <Button variant="outline" size="sm" @click="expand('all')">Expand all</Button>
        <Button variant="outline" size="sm" @click="expand('none')">Collapse all</Button>
      </div>

      <ul :key="drawn" class="grid gap-1 text-sm" aria-label="Items">
        <li v-for="node in tree" :key="itemKey(node.item)">
          <details :open="depth !== 'none'" class="group/item">
            <summary :class="summaryClass">
              <ChevronRightIcon class="size-4 shrink-0 transition-transform group-open/item:rotate-90" aria-hidden="true" />
              <span class="font-medium wrap-anywhere">{{ node.item.Name }}</span>
              <span class="text-xs text-muted-foreground">{{ typeNames[node.item.Type] ?? node.item.Type }}</span>
            </summary>

            <ul class="ml-4 grid gap-1 border-l pl-3">
              <li v-for="branch in node.roles" :key="branch.role.RoleID">
                <details :open="depth === 'all'" class="group/role">
                  <summary :class="summaryClass" class="flex-wrap">
                    <ChevronRightIcon class="size-4 shrink-0 transition-transform group-open/role:rotate-90" aria-hidden="true" />
                    <span class="wrap-anywhere">{{ branch.role.DisplayName }}</span>
                    <span class="flex flex-wrap gap-1">
                      <SecurityAccessBadge v-for="access in branch.access" :key="access.action" :access="access" :roles="roles" />
                    </span>
                  </summary>

                  <ul class="ml-4 grid gap-2 border-l py-1 pl-3">
                    <li v-for="access in branch.access" :key="access.action" class="grid gap-0.5">
                      <span class="font-mono text-xs font-medium">{{ access.action.toUpperCase() }}</span>
                      <span
                        v-for="(line, index) in describeAccess(access)"
                        :key="index"
                        class="text-xs wrap-anywhere"
                        :class="access.full ? 'text-muted-foreground' : 'text-warning'"
                      >
                        {{ line }}
                      </span>
                      <span v-if="access.inherited" class="text-xs text-muted-foreground">
                        Inherited from {{ access.from.map((id) => roleName(roles, id)).join(' and ') }}
                      </span>
                    </li>
                  </ul>
                </details>
              </li>
            </ul>
          </details>
        </li>
      </ul>
    </template>
  </FullScreenDialog>
</template>
