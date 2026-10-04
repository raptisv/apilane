<script setup lang="ts">
import {
  ArrowDownUpIcon,
  CheckIcon,
  DatabaseIcon,
  EllipsisVerticalIcon,
  LinkIcon,
  ListIcon,
  LockIcon,
  PencilIcon,
  TextCursorInputIcon,
  Trash2Icon,
} from '@lucide/vue'
import { computed } from 'vue'
import ConstraintBadge from '@/components/ConstraintBadge.vue'
import InlineAsync from '@/components/InlineAsync.vue'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuSeparator,
  DropdownMenuTrigger,
} from '@/components/ui/dropdown-menu'
import { TableCell, TableRow } from '@/components/ui/table'
import { useApplication } from '@/composables/useApplication'
import { useAsync } from '@/composables/useAsync'
import { apiServer } from '@/lib/apiServer'
import { formatCount } from '@/lib/entities'
import type { Entity } from '@/lib/entities'

// One entity in the entities list (EntityTable). The row loads its own record and history counts.
const props = defineProps<{ entity: Entity }>()
defineEmits<{ edit: []; rename: []; delete: [] }>()

const { application } = useApplication()

// The counts come straight from the API server, one call per row. A failure stays inside the row.
const counts = useAsync(() =>
  apiServer(application.value.Server.ServerUrl, application.value.Token).get<{ Data: number; History: number }>(
    '/api/Stats/CountDataAndHistory',
    { entity: props.entity.Name },
  ),
)

function entityRoute(name: string) {
  return { name, params: { appToken: application.value.Token, entity: props.entity.Name } }
}

const propertiesRoute = computed(() => entityRoute('app-entity-properties'))

// The data browser of the application, opened at this entity.
const entityDataRoute = computed(() => ({ name: 'app-data', params: { appToken: application.value.Token, entity: props.entity.Name } }))

// The security screen of the application, with this entity selected in its rule grid.
const securityRoute = computed(() => ({
  name: 'app-security',
  params: { appToken: application.value.Token },
  query: { item: `Entity-${props.entity.Name}` },
}))

// A disabled item stays readable, because it carries the reason under its label.
const disabledItemClass = 'items-start data-disabled:opacity-100 data-disabled:text-muted-foreground'
</script>

<template>
  <TableRow>
    <TableCell class="pl-4 align-top whitespace-normal">
      <RouterLink
        :to="propertiesRoute"
        class="rounded-sm font-medium wrap-anywhere text-link underline-offset-4 hover:underline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring"
      >
        {{ entity.Name }}
      </RouterLink>
      <p v-if="entity.Description" class="mt-0.5 text-xs wrap-anywhere text-muted-foreground">{{ entity.Description }}</p>

      <!-- On a narrow screen the other columns move under the name, so the table fits the screen. -->
      <div class="mt-1.5 flex flex-wrap items-center gap-x-2 gap-y-1.5 text-xs text-muted-foreground md:hidden">
        <span>
          Records
          <InlineAsync
            :loading="counts.loading.value"
            :error="counts.error.value"
            :label="`the record count of ${entity.Name}`"
            @retry="counts.reload()"
          >
            <span v-if="counts.data.value" class="font-mono text-foreground">{{ formatCount(counts.data.value.Data) }}</span>
          </InlineAsync>
        </span>
        <Badge v-if="entity.RequireChangeTracking" variant="secondary">
          Change tracking<template v-if="counts.data.value && counts.data.value.History > 0">
            · {{ formatCount(counts.data.value.History) }}</template
          >
        </Badge>
        <Badge v-if="application.DifferentiationEntity && entity.HasDifferentiationProperty" variant="secondary">
          Differentiation
        </Badge>
      </div>

      <!-- The constraints sit under the name at every width: a column of their own gets too narrow for them. -->
      <div v-if="entity.Constraints.length > 0" class="mt-1.5 flex flex-wrap gap-1">
        <ConstraintBadge v-for="(constraint, index) in entity.Constraints" :key="index" :constraint="constraint" />
      </div>
    </TableCell>

    <TableCell class="hidden text-right align-top md:table-cell">
      <InlineAsync
        :loading="counts.loading.value"
        :error="counts.error.value"
        :label="`the record count of ${entity.Name}`"
        @retry="counts.reload()"
      >
        <span v-if="counts.data.value" class="font-mono">{{ formatCount(counts.data.value.Data) }}</span>
      </InlineAsync>
    </TableCell>

    <TableCell class="hidden align-top md:table-cell">
      <template v-if="entity.RequireChangeTracking">
        <Badge variant="secondary">On</Badge>
        <span
          v-if="counts.data.value && counts.data.value.History > 0"
          class="ml-1.5 text-xs text-muted-foreground"
          :title="`${formatCount(counts.data.value.History)} tracked changes`"
        >
          {{ formatCount(counts.data.value.History) }}
          <span class="sr-only">tracked changes</span>
        </span>
      </template>
    </TableCell>

    <TableCell v-if="application.DifferentiationEntity" class="hidden align-top md:table-cell">
      <template v-if="entity.HasDifferentiationProperty">
        <CheckIcon class="size-4" aria-hidden="true" />
        <span class="sr-only">Yes</span>
      </template>
    </TableCell>

    <TableCell class="pr-4 text-right align-top">
      <DropdownMenu>
        <DropdownMenuTrigger as-child>
          <Button variant="ghost" size="icon-sm">
            <EllipsisVerticalIcon />
            <span class="sr-only">Actions for {{ entity.Name }}</span>
          </Button>
        </DropdownMenuTrigger>

        <DropdownMenuContent align="end" class="w-60">
          <DropdownMenuItem as-child>
            <RouterLink :to="propertiesRoute"><ListIcon />Properties</RouterLink>
          </DropdownMenuItem>
          <!-- A system entity shows its constraints read-only to anyone but an administrator. -->
          <DropdownMenuItem as-child>
            <RouterLink :to="entityRoute('app-entity-constraints')"><LinkIcon />Constraints</RouterLink>
          </DropdownMenuItem>
          <DropdownMenuItem as-child>
            <RouterLink :to="entityRoute('app-entity-sorting')"><ArrowDownUpIcon />Default sorting</RouterLink>
          </DropdownMenuItem>
          <DropdownMenuItem as-child>
            <RouterLink :to="entityDataRoute"><DatabaseIcon />Data</RouterLink>
          </DropdownMenuItem>
          <DropdownMenuItem as-child>
            <RouterLink :to="securityRoute"><LockIcon />Security</RouterLink>
          </DropdownMenuItem>

          <DropdownMenuSeparator />

          <DropdownMenuItem @select="$emit('edit')">
            <PencilIcon />
            Edit
          </DropdownMenuItem>
          <DropdownMenuItem :disabled="entity.IsSystem" :class="entity.IsSystem && disabledItemClass" @select="$emit('rename')">
            <TextCursorInputIcon :class="entity.IsSystem && 'mt-0.5'" />
            <span>Rename<span v-if="entity.IsSystem" class="block text-xs">Cannot rename a system entity</span></span>
          </DropdownMenuItem>

          <DropdownMenuSeparator />

          <DropdownMenuItem
            :variant="entity.IsSystem ? 'default' : 'destructive'"
            :disabled="entity.IsSystem"
            :class="entity.IsSystem && disabledItemClass"
            @select="$emit('delete')"
          >
            <Trash2Icon :class="entity.IsSystem && 'mt-0.5'" />
            <span>Delete<span v-if="entity.IsSystem" class="block text-xs">Cannot delete a system entity</span></span>
          </DropdownMenuItem>
        </DropdownMenuContent>
      </DropdownMenu>
    </TableCell>
  </TableRow>
</template>
