<script setup lang="ts">
import { computed } from 'vue'
import { useRouter } from 'vue-router'
import type { RouteLocationRaw } from 'vue-router'
import { Label } from '@/components/ui/label'
import { Select, SelectContent, SelectGroup, SelectItem, SelectLabel, SelectTrigger, SelectValue } from '@/components/ui/select'
import type { Entity } from '@/lib/entities'

// Picks the entity of a data browser: a row of links on a wide screen, a select on a phone. Each
// entity is its own address (`to`), so the application's and the administrator's data browser
// (with their own routes) both use it.
const props = defineProps<{
  entities: readonly Entity[]
  /** The name of the entity shown, if any. */
  current?: string
  to: (entity: string) => RouteLocationRaw
}>()

const router = useRouter()

const groups = computed(() =>
  [
    { label: 'Custom entities', entities: props.entities.filter((entity) => !entity.IsSystem) },
    { label: 'System entities', entities: props.entities.filter((entity) => entity.IsSystem) },
  ].filter((group) => group.entities.length > 0),
)

const selected = computed({
  get: () => props.current ?? '',
  set: (name: string) => void router.push(props.to(name)),
})

const linkClass =
  'inline-flex items-center rounded-md px-2.5 py-1 text-sm whitespace-nowrap transition-colors focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring'
const activeClass = 'bg-secondary font-medium text-secondary-foreground'
const inactiveClass = 'text-muted-foreground hover:bg-muted hover:text-foreground'
</script>

<template>
  <!-- Phone: a select. -->
  <div class="grid gap-1.5 sm:hidden">
    <Label for="entity-switcher">Entity</Label>
    <Select v-model="selected">
      <SelectTrigger id="entity-switcher" class="w-full">
        <SelectValue placeholder="Select an entity" />
      </SelectTrigger>
      <SelectContent>
        <SelectGroup v-for="group in groups" :key="group.label">
          <SelectLabel>{{ group.label }}</SelectLabel>
          <SelectItem v-for="entity in group.entities" :key="entity.Name" :value="entity.Name">{{ entity.Name }}</SelectItem>
        </SelectGroup>
      </SelectContent>
    </Select>
  </div>

  <!-- Wide screen: the entities as links, custom ones first. They wrap when they do not fit. -->
  <nav aria-label="Entities" class="hidden sm:block">
    <ul class="flex flex-wrap items-center gap-1">
      <template v-for="(group, index) in groups" :key="group.label">
        <li v-if="index > 0" aria-hidden="true" class="mx-1 h-5 w-px bg-border" />
        <li v-for="entity in group.entities" :key="entity.Name">
          <RouterLink
            :to="to(entity.Name)"
            :class="[linkClass, entity.Name === current ? activeClass : inactiveClass]"
            :aria-current="entity.Name === current ? 'page' : undefined"
          >
            {{ entity.Name }}
          </RouterLink>
        </li>
      </template>
    </ul>
  </nav>
</template>
