<script setup lang="ts">
import { ChevronRightIcon, CircleAlertIcon, Grid3x3Icon, NetworkIcon, TriangleAlertIcon } from '@lucide/vue'
import { computed, ref, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import UnsavedChangesBar from '@/components/UnsavedChangesBar.vue'
import { Alert, AlertDescription, AlertTitle } from '@/components/ui/alert'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@/components/ui/table'
import { useMutation } from '@/composables/useMutation'
import { api, unwrap } from '@/lib/api'
import type { Schemas } from '@/lib/api'
import { listErrors } from '@/lib/forms'
import { changedItems, changesText, restoreRule, rulesRequest, setRule, toEditable } from '@/lib/security'
import { findRule, inheritance, isCell, itemActions, itemKey, roleName } from '@/lib/securityAccess'
import type { Action, SecurityItem, SecurityRule } from '@/lib/securityAccess'
import * as toast from '@/lib/toast'
import SecurityItemPicker from './SecurityItemPicker.vue'
import SecurityMatrixDialog from './SecurityMatrixDialog.vue'
import SecurityRuleCell from './SecurityRuleCell.vue'
import SecurityTreeDialog from './SecurityTreeDialog.vue'

// The rules of the security screen: one item at a time (?item=Entity-Customers), a role x action
// grid for it, and the tree and matrix views (?view=tree, ?view=matrix). The rules of every item are
// edited here and saved together with one Save, which replaces every stored rule.
const props = defineProps<{
  appToken: string
  items: SecurityItem[]
  roles: Schemas['SecurityRoleResponse'][]
  rolesAvailable: boolean
  saved: Schemas['SecurityRuleResponse'][]
}>()

const emit = defineEmits<{
  saved: [rules: Schemas['SecurityRuleResponse'][]]
  /** Whether rules are waiting to be saved, for the settings form next to this editor. */
  dirty: [value: boolean]
}>()

const route = useRoute()
const router = useRouter()

const savedRules = computed(() => props.saved.map(toEditable))
const rules = ref<SecurityRule[]>([])

// The rules of the cells switched off since the last save or discard, by cell (cellKey): switching
// such a cell on again brings back its properties and rate limit.
const switchedOff = new Map<string, SecurityRule>()

// Runs on the first load and after every save (the PUT answers the stored rules).
watch(
  savedRules,
  (saved) => {
    rules.value = saved.map((rule) => ({ ...rule }))
    switchedOff.clear()
  },
  { immediate: true },
)

const changed = computed(() => changedItems(savedRules.value, rules.value))
const dirty = computed(() => changed.value.size > 0)

watch(dirty, (value) => emit('dirty', value))

// The item in the address; an unknown or missing one falls back to the first (the schema).
const selected = computed<SecurityItem | undefined>(
  () => props.items.find((item) => itemKey(item) === route.query.item) ?? props.items[0],
)

function select(key: string): void {
  void router.replace({ query: { ...route.query, item: key } })
}

const actions = computed<Action[]>(() => (selected.value ? itemActions(selected.value) : []))

const save = useMutation(async () => {
  const result = await unwrap(
    api.PUT('/api/v1/applications/{appToken}/security/rules', {
      params: { path: { appToken: props.appToken } },
      body: rulesRequest(rules.value),
    }),
  )
  emit('saved', result.Rules)
})

// The API names the rule it stopped at by its place in the request, which is `rules` in its order.
const errors = computed(() => listErrors(save.error.value, 'Rules'))
const failedPlace = computed(() => {
  const places = Object.keys(errors.value.rows).map(Number)
  return places.length > 0 ? places[0] : undefined
})
const failedRule = computed(() => (failedPlace.value === undefined ? undefined : rules.value[failedPlace.value]))
const failedItem = computed(() => {
  const rule = failedRule.value
  return rule ? props.items.find((item) => itemKey(item) === itemKey(rule)) : undefined
})

function cellError(roleId: string, action: string): string | undefined {
  const rule = failedRule.value

  if (!rule || failedPlace.value === undefined || !selected.value || !isCell(rule, selected.value, roleId, action)) {
    return undefined
  }

  return errors.value.rows[failedPlace.value]
}

// Which cells show their details, by item, role and action. A cell opens when it is switched on.
const expanded = ref(new Set<string>())

function cellKey(roleId: string, action: string): string {
  return `${selected.value ? itemKey(selected.value) : ''}|${roleId}|${action}`
}

function setExpanded(roleId: string, action: string, open: boolean): void {
  const next = new Set(expanded.value)

  if (open) {
    next.add(cellKey(roleId, action))
  } else {
    next.delete(cellKey(roleId, action))
  }

  expanded.value = next
}

// Any edit makes the message of the last save stale: the places in the list move.
function change(roleId: string, action: string, next: SecurityRule | undefined): void {
  const item = selected.value

  if (!item) {
    return
  }

  const current = findRule(rules.value, item, roleId, action)
  let rule = next

  if (!next && current) {
    switchedOff.set(cellKey(roleId, action), current)
  } else if (next && !current) {
    rule = restoreRule(next, switchedOff.get(cellKey(roleId, action)))
  }

  rules.value = setRule(rules.value, item, roleId, action, rule)
  save.reset()
}

function discard(): void {
  rules.value = savedRules.value.map((rule) => ({ ...rule }))
  switchedOff.clear()
  save.reset()
}

// Opens the item and the cell the save stopped at.
function showFailed(): void {
  const rule = failedRule.value

  if (!rule || !failedItem.value) {
    return
  }

  select(itemKey(failedItem.value))
  expanded.value = new Set([...expanded.value, `${itemKey(rule)}|${rule.RoleID}|${rule.Action.toLowerCase()}`])
}

async function submit(): Promise<void> {
  if (await save.run()) {
    toast.success('Security rules saved.')
  } else {
    showFailed()
  }
}

// The tree and the matrix are open while the address says so: ?view=tree, ?view=matrix.
function viewOpen(view: string) {
  return computed({
    get: () => route.query.view === view,
    set: (open: boolean) => {
      const query = { ...route.query }

      if (open) {
        query.view = view
      } else {
        delete query.view
      }

      void router.replace({ query })
    },
  })
}

const treeOpen = viewOpen('tree')
const matrixOpen = viewOpen('matrix')

const roleHelp: Record<string, string> = {
  Anonymous: 'Any call without an auth token, or with one that has expired.',
  Authenticated: 'Any call with a valid auth token: every signed-in user, whatever their roles.',
  Role: 'Signed-in users who have this role.',
}

const typeNames: Record<string, string> = { Schema: 'The application schema', Entity: 'Entity', CustomEndpoint: 'Custom endpoint' }
</script>

<template>
  <section aria-labelledby="rules-title" class="grid min-w-0 gap-4">
    <div class="flex flex-wrap items-start justify-between gap-x-6 gap-y-3">
      <div class="min-w-0">
        <h2 id="rules-title" class="text-sm font-semibold">Access rules</h2>
        <p class="mt-1 text-xs text-muted-foreground">Set which roles may call what, for each entity and custom endpoint.</p>
      </div>
      <div class="flex shrink-0 gap-2">
        <Button as-child variant="outline" size="sm">
          <RouterLink :to="{ query: { ...route.query, view: 'tree' } }" replace><NetworkIcon />Tree</RouterLink>
        </Button>
        <Button as-child variant="outline" size="sm">
          <RouterLink :to="{ query: { ...route.query, view: 'matrix' } }" replace><Grid3x3Icon />Matrix</RouterLink>
        </Button>
      </div>
    </div>

    <Alert v-if="!rolesAvailable" role="note" class="border-warning/40 text-warning">
      <TriangleAlertIcon />
      <AlertTitle>The roles could not be loaded from the API server</AlertTitle>
      <AlertDescription>
        Only Anonymous, Authenticated and the roles already used in rules are listed, and they can still be edited. Reload
        the page once the API server answers to see the roles of the application's users.
      </AlertDescription>
    </Alert>

    <details class="group rounded-lg border bg-card px-4 py-3 text-sm">
      <summary class="flex cursor-pointer items-center gap-1.5 font-medium select-none">
        <ChevronRightIcon class="size-4 transition-transform group-open:rotate-90" aria-hidden="true" />
        How rules add up
      </summary>
      <ul class="mt-3 grid list-disc gap-1.5 pl-5 text-muted-foreground">
        <li>Without a rule there is no access.</li>
        <li>
          A call without a valid auth token gets what Anonymous has. A signed-in user gets what Anonymous, Authenticated
          and each of their roles have, together: a role inherits what Authenticated has.
        </li>
        <li>
          The properties of those rules add up. The ID is always included. A rule with no properties returns only the ID
          on a get and refuses a post or put.
        </li>
        <li>If any of those rules allows only the owner's records, the user gets only their own records.</li>
        <li>
          A rate limit counts per signed-in user; calls without a valid auth token all share one count. It covers the
          item and action as a whole, whatever the role: the most generous limit of its rules applies, and none at all if
          one of its rules has no limit.
        </li>
      </ul>
    </details>

    <Alert v-if="errors.message || failedRule" variant="destructive" role="alert">
      <CircleAlertIcon />
      <AlertTitle>The rules were not saved</AlertTitle>
      <AlertDescription>
        <p v-if="errors.message">{{ errors.message }}</p>
        <template v-if="failedRule && failedPlace !== undefined">
          <p>
            {{ failedRule.Name }} · {{ roleName(roles, failedRule.RoleID) }} · {{ failedRule.Action.toUpperCase() }}:
            {{ errors.rows[failedPlace] }}
          </p>
          <Button
            v-if="failedItem && selected && itemKey(failedItem) !== itemKey(selected)"
            variant="outline"
            size="sm"
            class="mt-2"
            @click="showFailed"
          >
            Show {{ failedRule.Name }}
          </Button>
        </template>
      </AlertDescription>
    </Alert>

    <div class="grid min-w-0 gap-4 lg:grid-cols-[13rem_minmax(0,1fr)] lg:items-start">
      <SecurityItemPicker
        :items="items"
        :selected="selected ? itemKey(selected) : ''"
        :changed="changed"
        :failed="failedItem ? itemKey(failedItem) : undefined"
        @select="select"
      />

      <div v-if="selected" class="grid min-w-0 gap-3">
        <div>
          <h3 class="text-sm font-medium wrap-anywhere">{{ selected.Name }}</h3>
          <p class="text-xs text-muted-foreground">
            {{ typeNames[selected.Type] ?? selected.Type }}{{ selected.IsSystem ? ' (system)' : '' }}
          </p>
        </div>

        <!-- The grid scrolls sideways on its own on a narrow screen; the role column stays in view. -->
        <div class="min-w-0 rounded-lg border bg-card">
          <Table class="min-w-max">
            <TableHeader>
              <TableRow>
                <TableHead class="sticky left-0 z-10 bg-card pl-4">Role</TableHead>
                <TableHead v-for="action in actions" :key="action" class="min-w-44">{{ action.toUpperCase() }}</TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              <TableRow v-for="role in roles" :key="role.RoleID" class="hover:bg-transparent">
                <TableHead scope="row" class="sticky left-0 z-10 h-auto w-40 max-w-40 bg-card py-3 pl-4 align-top whitespace-normal">
                  <span class="block font-medium wrap-anywhere">{{ role.DisplayName }}</span>
                  <span class="mt-0.5 block text-xs font-normal text-muted-foreground">{{ roleHelp[role.Kind] }}</span>
                  <Badge
                    v-if="role.Orphaned"
                    variant="outline"
                    class="mt-1.5 text-warning"
                    :title="rolesAvailable ? 'No user of the application has this role.' : 'Could not be checked: the API server did not answer.'"
                  >
                    {{ rolesAvailable ? 'No user has it' : 'Not checked' }}
                  </Badge>
                </TableHead>
                <TableCell v-for="action in actions" :key="action" class="py-3 align-top whitespace-normal">
                  <SecurityRuleCell
                    :item="selected"
                    :role="role"
                    :action="action"
                    :rule="findRule(rules, selected, role.RoleID, action)"
                    :inheritance="inheritance(rules, selected, role.RoleID, action)"
                    :error="cellError(role.RoleID, action)"
                    :disabled="save.pending.value"
                    :expanded="expanded.has(cellKey(role.RoleID, action))"
                    @update:expanded="setExpanded(role.RoleID, action, $event)"
                    @change="change(role.RoleID, action, $event)"
                  />
                </TableCell>
              </TableRow>
            </TableBody>
          </Table>
        </div>
      </div>
    </div>

    <UnsavedChangesBar
      :dirty="dirty"
      :pending="save.pending.value"
      :message="changesText(changed.size)"
      @save="submit"
      @discard="discard"
    />

    <SecurityTreeDialog v-model:open="treeOpen" :items="items" :roles="roles" :rules="savedRules" :dirty="dirty" />
    <SecurityMatrixDialog v-model:open="matrixOpen" :items="items" :roles="roles" :rules="savedRules" :dirty="dirty" />
  </section>
</template>
