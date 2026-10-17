<script setup lang="ts">
import { CheckCheckIcon, EllipsisVerticalIcon, LinkIcon, PencilIcon, TextCursorInputIcon, Trash2Icon } from '@lucide/vue'
import { computed } from 'vue'
import AccessBadge from '@/components/AccessBadge.vue'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuSeparator,
  DropdownMenuTrigger,
} from '@/components/ui/dropdown-menu'
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@/components/ui/table'
import { useAccessSummaries } from '@/composables/useAccessSummaries'
import { useApplication } from '@/composables/useApplication'
import { useEntity } from '@/composables/useEntity'
import { propertyConstraints } from '@/lib/entities'
import type { Constraint } from '@/lib/entities'
import { propertyRules } from '@/lib/properties'
import type { Property } from '@/lib/properties'

// One of the two tables of the properties screen (custom properties, system properties), in the
// order the API sends them. A property that is part of a constraint of the entity (unique, or a foreign
// key) shows it next to its name.
const props = defineProps<{ properties: Property[]; constraints?: readonly Constraint[] }>()
defineEmits<{ edit: [property: Property]; rename: [property: Property]; delete: [property: Property] }>()

function inConstraint(property: Property): boolean {
  return propertyConstraints(property.Name, props.constraints ?? []).length > 0
}

const constraintReason = 'Part of a constraint: remove it first'

// A disabled item stays readable, because it carries the reason under its label.
const disabledItemClass = 'items-start data-disabled:opacity-100 data-disabled:text-muted-foreground'

// Who may read or write a property: an icon next to its name, leading to the rules of the entity.
const { application } = useApplication()
const { entity } = useEntity()
const access = useAccessSummaries()

const securityRoute = computed(() => ({
  name: 'app-security',
  params: { appToken: application.value.Token },
  query: { item: `Entity-${entity.value.Name}` },
}))
</script>

<template>
  <div class="overflow-hidden rounded-lg border bg-card">
    <Table class="table-fixed">
      <TableHeader>
        <TableRow>
          <TableHead class="w-12 pl-4"><span class="sr-only">Access</span></TableHead>
          <TableHead class="pl-2">Property</TableHead>
          <TableHead class="hidden w-28 md:table-cell">Type</TableHead>
          <TableHead class="hidden w-2/5 md:table-cell">Rules</TableHead>
          <TableHead class="w-14 pr-4"><span class="sr-only">Actions</span></TableHead>
        </TableRow>
      </TableHeader>
      <TableBody>
        <TableRow v-for="property in properties" :key="property.Name">
          <TableCell class="pl-4 align-top">
            <AccessBadge :summary="access.property(entity.Name, property.Name)" :to="securityRoute" :subject="property.Name" />
          </TableCell>
          <TableCell class="pl-2 align-top whitespace-normal">
            <div class="flex flex-wrap items-center gap-x-2 gap-y-1">
              <span class="font-medium wrap-anywhere">{{ property.Name }}</span>
              <Badge v-if="property.IsPrimaryKey" variant="outline" class="border-link/40 text-link">Primary key</Badge>
              <Badge v-if="property.Required && !property.IsPrimaryKey" variant="secondary">Required</Badge>
              <Badge v-if="property.Encrypted" variant="outline" class="border-warning/40 text-warning">Encrypted</Badge>
              <Badge
                v-for="(constraint, index) in propertyConstraints(property.Name, constraints ?? [])"
                :key="index"
                variant="outline"
                class="h-auto max-w-full shrink justify-start whitespace-normal"
                :class="constraint.type === 'ForeignKey' ? 'border-warning/40 text-warning' : 'border-link/40 text-link'"
                :title="constraint.description"
              >
                <component :is="constraint.type === 'ForeignKey' ? LinkIcon : CheckCheckIcon" aria-hidden="true" class="shrink-0" />
                <span aria-hidden="true" class="wrap-anywhere">{{ constraint.label }}</span>
                <span class="sr-only">{{ constraint.description }}</span>
              </Badge>
            </div>
            <p v-if="property.Description" class="mt-0.5 text-xs wrap-anywhere text-muted-foreground">
              {{ property.Description }}
            </p>

            <!-- On a narrow screen the other columns move under the name, so the table fits the screen. -->
            <div class="mt-1.5 flex flex-wrap items-center gap-1 md:hidden">
              <Badge variant="secondary">{{ property.Type }}</Badge>
              <Badge
                v-for="rule in propertyRules(property)"
                :key="rule"
                variant="outline"
                class="h-auto max-w-full shrink justify-start font-normal wrap-anywhere whitespace-normal"
              >
                {{ rule }}
              </Badge>
            </div>
          </TableCell>

          <TableCell class="hidden align-top md:table-cell">
            <Badge variant="secondary">{{ property.Type }}</Badge>
          </TableCell>

          <TableCell class="hidden align-top whitespace-normal md:table-cell">
            <div class="flex flex-wrap gap-1">
              <Badge
                v-for="rule in propertyRules(property)"
                :key="rule"
                variant="outline"
                class="h-auto max-w-full shrink justify-start font-normal wrap-anywhere whitespace-normal"
              >
                {{ rule }}
              </Badge>
            </div>
          </TableCell>

          <TableCell class="pr-4 text-right align-top">
            <!-- A system property cannot be edited, renamed or deleted, so it gets no menu. -->
            <DropdownMenu v-if="!property.IsSystem">
              <DropdownMenuTrigger as-child>
                <Button variant="ghost" size="icon-sm">
                  <EllipsisVerticalIcon />
                  <span class="sr-only">Actions for {{ property.Name }}</span>
                </Button>
              </DropdownMenuTrigger>

              <!-- The API refuses to rename or delete a property a constraint names: the items say so instead. -->
              <DropdownMenuContent align="end" class="w-60">
                <DropdownMenuItem @select="$emit('edit', property)">
                  <PencilIcon />
                  Edit
                </DropdownMenuItem>
                <DropdownMenuItem
                  :disabled="inConstraint(property)"
                  :class="inConstraint(property) && disabledItemClass"
                  @select="$emit('rename', property)"
                >
                  <TextCursorInputIcon :class="inConstraint(property) && 'mt-0.5'" />
                  <span>Rename<span v-if="inConstraint(property)" class="block text-xs">{{ constraintReason }}</span></span>
                </DropdownMenuItem>

                <DropdownMenuSeparator />

                <DropdownMenuItem
                  :variant="inConstraint(property) ? 'default' : 'destructive'"
                  :disabled="inConstraint(property)"
                  :class="inConstraint(property) && disabledItemClass"
                  @select="$emit('delete', property)"
                >
                  <Trash2Icon :class="inConstraint(property) && 'mt-0.5'" />
                  <span>Delete<span v-if="inConstraint(property)" class="block text-xs">{{ constraintReason }}</span></span>
                </DropdownMenuItem>
              </DropdownMenuContent>
            </DropdownMenu>
          </TableCell>
        </TableRow>
      </TableBody>
    </Table>
  </div>
</template>
