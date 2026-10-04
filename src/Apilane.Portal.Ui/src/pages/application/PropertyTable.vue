<script setup lang="ts">
import { EllipsisVerticalIcon, PencilIcon, TextCursorInputIcon, Trash2Icon } from '@lucide/vue'
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
import { propertyRules } from '@/lib/properties'
import type { Property } from '@/lib/properties'

// One of the two tables of the properties screen (custom properties, system properties), in the
// order the API sends them.
defineProps<{ properties: Property[] }>()
defineEmits<{ edit: [property: Property]; rename: [property: Property]; delete: [property: Property] }>()
</script>

<template>
  <div class="overflow-hidden rounded-lg border bg-card">
    <Table class="table-fixed">
      <TableHeader>
        <TableRow>
          <TableHead class="pl-4">Property</TableHead>
          <TableHead class="hidden w-28 md:table-cell">Type</TableHead>
          <TableHead class="hidden w-2/5 md:table-cell">Rules</TableHead>
          <TableHead class="w-14 pr-4"><span class="sr-only">Actions</span></TableHead>
        </TableRow>
      </TableHeader>
      <TableBody>
        <TableRow v-for="property in properties" :key="property.Name">
          <TableCell class="pl-4 align-top whitespace-normal">
            <div class="flex flex-wrap items-center gap-x-2 gap-y-1">
              <span class="font-medium wrap-anywhere">{{ property.Name }}</span>
              <Badge v-if="property.IsPrimaryKey" variant="outline" class="border-link/40 text-link">Primary key</Badge>
              <Badge v-if="property.Required && !property.IsPrimaryKey" variant="secondary">Required</Badge>
              <Badge v-if="property.Encrypted" variant="outline" class="border-warning/40 text-warning">Encrypted</Badge>
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

              <DropdownMenuContent align="end">
                <DropdownMenuItem @select="$emit('edit', property)">
                  <PencilIcon />
                  Edit
                </DropdownMenuItem>
                <DropdownMenuItem @select="$emit('rename', property)">
                  <TextCursorInputIcon />
                  Rename
                </DropdownMenuItem>

                <DropdownMenuSeparator />

                <DropdownMenuItem variant="destructive" @select="$emit('delete', property)">
                  <Trash2Icon />
                  Delete
                </DropdownMenuItem>
              </DropdownMenuContent>
            </DropdownMenu>
          </TableCell>
        </TableRow>
      </TableBody>
    </Table>
  </div>
</template>
