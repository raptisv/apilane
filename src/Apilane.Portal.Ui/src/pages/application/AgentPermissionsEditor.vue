<script setup lang="ts">
import { TriangleAlertIcon } from '@lucide/vue'
import { computed, useId } from 'vue'
import { Checkbox } from '@/components/ui/checkbox'
import { Label } from '@/components/ui/label'
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'
import { agentAccessLevel, setAgentAccessLevel } from '@/lib/agentPermissions'
import type { AgentPermissionGrant, AgentPermissionResource } from '@/lib/agentPermissions'

const props = defineProps<{ resources: AgentPermissionResource[] }>()
const grants = defineModel<AgentPermissionGrant[]>({ required: true })
const id = useId()
const canDelete = computed(() => grants.value.some((grant) => grant.Delete))
const canRebuild = computed(() => grants.value.some((grant) => grant.Resource === 'rebuild' && grant.Write))

function grantFor(resource: AgentPermissionResource): AgentPermissionGrant {
  return grants.value.find((grant) => grant.Resource === resource.Resource)
    ?? { Resource: resource.Resource, Read: false, Write: false, Delete: false }
}

function replace(grant: AgentPermissionGrant): void {
  grants.value = [...grants.value.filter((item) => item.Resource !== grant.Resource), grant]
}

function setLevel(resource: AgentPermissionResource, value: unknown): void {
  if (value === 'none' || value === 'read' || value === 'write') {
    replace(setAgentAccessLevel(grantFor(resource), resource, value))
  }
}

function setDelete(resource: AgentPermissionResource, value: boolean | 'indeterminate'): void {
  const grant = grantFor(resource)
  replace({ ...grant, Delete: resource.CanDelete && grant.Read && value === true })
}
</script>

<template>
  <fieldset class="grid min-w-0 gap-3">
    <legend class="mb-2 text-sm font-semibold">Agent rights for this application</legend>
    <p class="text-xs text-muted-foreground">
      None denies access to the area. Read allows inspection. Write allows changes and includes read where available.
      Deletion requires a separate grant. The application summary remains visible to every collaborator.
    </p>

    <div class="divide-y rounded-lg border">
      <div v-for="resource in props.resources" :key="resource.Resource" class="grid gap-3 p-3 sm:grid-cols-[1fr_10rem]">
        <div class="min-w-0">
          <Label :for="`${id}-${resource.Resource}`">{{ resource.Name }}</Label>
          <p :id="`${id}-${resource.Resource}-description`" class="mt-1 text-xs text-muted-foreground">{{ resource.Description }}</p>
        </div>
        <div class="grid gap-2 self-start">
          <Select :model-value="agentAccessLevel(grantFor(resource))" @update:model-value="setLevel(resource, $event)">
            <SelectTrigger
              :id="`${id}-${resource.Resource}`"
              :aria-describedby="`${id}-${resource.Resource}-description`"
              class="w-full"
            >
              <SelectValue />
            </SelectTrigger>
            <SelectContent>
              <SelectItem value="none">None</SelectItem>
              <SelectItem v-if="resource.CanRead" value="read">Read</SelectItem>
              <SelectItem v-if="resource.CanWrite" value="write">{{ resource.CanRead ? 'Read and write' : 'Allow' }}</SelectItem>
            </SelectContent>
          </Select>
          <template v-if="resource.CanDelete">
            <div class="flex items-center gap-2">
              <Checkbox
                :id="`${id}-${resource.Resource}-delete`"
                :model-value="grantFor(resource).Delete"
                :disabled="!grantFor(resource).Read"
                :aria-describedby="!grantFor(resource).Read ? `${id}-${resource.Resource}-delete-help` : undefined"
                @update:model-value="setDelete(resource, $event)"
              />
              <Label :for="`${id}-${resource.Resource}-delete`" class="text-xs">
                Allow deletion<span class="sr-only">: {{ resource.Name }}</span>
              </Label>
            </div>
            <p v-if="!grantFor(resource).Read" :id="`${id}-${resource.Resource}-delete-help`" class="text-xs text-muted-foreground">
              Select Read or Read and write first.
            </p>
          </template>
        </div>
      </div>
    </div>

    <div v-if="canDelete || canRebuild" role="note" class="flex gap-2.5 rounded-lg border border-warning/40 bg-warning/10 p-3 text-sm text-warning">
      <TriangleAlertIcon class="mt-0.5 size-4 shrink-0" aria-hidden="true" />
      <div class="grid gap-1">
        <p v-if="canDelete">The agent can delete the selected resources without confirmation. Deletion cannot be undone.</p>
        <p v-if="canRebuild">The agent can rebuild this application without confirmation, permanently removing all its data.</p>
      </div>
    </div>
  </fieldset>
</template>
