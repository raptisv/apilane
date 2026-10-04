<script setup lang="ts">
import { ShieldOffIcon } from '@lucide/vue'
import { computed } from 'vue'
import FullScreenDialog from '@/components/FullScreenDialog.vue'
import StateMessage from '@/components/StateMessage.vue'
import { Badge } from '@/components/ui/badge'
import { itemAccess, itemKey } from '@/lib/securityAccess'
import type { SecurityItem, SecurityRole, SecurityRule } from '@/lib/securityAccess'
import SecurityAccessBadge from './SecurityAccessBadge.vue'

// The saved rules as a table, read-only: a row per item, a column per role, and in each cell the
// actions that role may call. Roles include what they inherit from Anonymous and Authenticated.
const props = defineProps<{
  items: SecurityItem[]
  roles: SecurityRole[]
  rules: SecurityRule[]
  /** The editor holds changes that are not saved (and so not shown here). */
  dirty: boolean
}>()

const open = defineModel<boolean>('open', { required: true })

const groupNames: Record<string, string> = { Schema: 'Schema', Entity: 'Entities', CustomEndpoint: 'Custom endpoints' }

const groups = computed(() =>
  ['Schema', 'Entity', 'CustomEndpoint']
    .map((type) => ({
      type,
      label: groupNames[type] ?? type,
      rows: props.items
        .filter((item) => item.Type === type)
        .map((item) => ({
          item,
          cells: props.roles.map((role) => ({
            role,
            access: itemAccess(props.rules, item, role.RoleID).filter((access) => access.allowed),
          })),
        })),
    }))
    .filter((group) => group.rows.length > 0),
)

// The first column and the header row stay in view while the table scrolls (the dialog body scrolls).
const stickyColumn = 'sticky left-0 z-10 bg-popover'
</script>

<template>
  <FullScreenDialog
    v-model:open="open"
    title="Security matrix"
    description="What each role may call on each item, as the API server applies the saved rules. A role includes what it inherits from Anonymous and Authenticated."
  >
    <template #header>
      <p v-if="dirty" class="text-xs text-warning">Shows the saved rules: your unsaved changes are not included.</p>
    </template>

    <StateMessage v-if="groups.length === 0 || roles.length === 0" :icon="ShieldOffIcon" title="No entities or roles configured" />

    <template v-else>
      <div class="relative">
        <table class="min-w-max border-separate border-spacing-0 text-sm">
          <thead>
            <tr>
              <th scope="col" :class="stickyColumn" class="top-0 z-20 border-b px-3 py-2 text-left font-medium">Item</th>
              <th
                v-for="role in roles"
                :key="role.RoleID"
                scope="col"
                class="sticky top-0 z-10 max-w-48 border-b bg-popover px-3 py-2 text-left font-medium wrap-anywhere"
              >
                {{ role.DisplayName }}
              </th>
            </tr>
          </thead>
          <tbody v-for="group in groups" :key="group.type">
            <tr>
              <th
                scope="rowgroup"
                :colspan="roles.length + 1"
                class="border-b bg-muted/40 px-3 py-1.5 text-left text-xs font-medium text-muted-foreground"
              >
                {{ group.label }}
              </th>
            </tr>
            <tr v-for="row in group.rows" :key="itemKey(row.item)">
              <th scope="row" :class="stickyColumn" class="max-w-56 border-b px-3 py-2 text-left font-medium wrap-anywhere">
                {{ row.item.Name }}
              </th>
              <td v-for="cell in row.cells" :key="cell.role.RoleID" class="border-b px-3 py-2 align-top">
                <span v-if="cell.access.length > 0" class="flex flex-wrap gap-1">
                  <SecurityAccessBadge v-for="access in cell.access" :key="access.action" :access="access" :roles="roles" />
                </span>
                <span v-else class="text-muted-foreground">
                  <span aria-hidden="true">–</span>
                  <span class="sr-only">No access</span>
                </span>
              </td>
            </tr>
          </tbody>
        </table>
      </div>

      <div class="mt-4 flex flex-wrap items-center gap-x-4 gap-y-2 text-xs text-muted-foreground">
        <span class="flex items-center gap-1.5">
          <Badge variant="outline" class="border-success/50 font-mono text-success">GET</Badge>
          Full access
        </span>
        <span class="flex items-center gap-1.5">
          <Badge variant="outline" class="border-warning/50 font-mono text-warning">GET</Badge>
          Restricted: owned records only, or some or no properties
        </span>
        <span class="flex items-center gap-1.5">
          <Badge variant="outline" class="border-dashed font-mono">GET</Badge>
          Inherited from Anonymous or Authenticated
        </span>
        <span>Hover a badge, or open the tree view, for its details.</span>
      </div>
    </template>
  </FullScreenDialog>
</template>
