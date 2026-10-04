<script setup lang="ts">
import { ArrowLeftIcon, CompassIcon } from '@lucide/vue'
import { computed } from 'vue'
import { useRoute } from 'vue-router'
import type { RouteLocationRaw } from 'vue-router'
import ErrorState from '@/components/ErrorState.vue'
import LoadingState from '@/components/LoadingState.vue'
import PageHeader from '@/components/PageHeader.vue'
import StateMessage from '@/components/StateMessage.vue'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { useApplication } from '@/composables/useApplication'
import { provideEntity } from '@/composables/useEntity'
import { ApiError } from '@/lib/api'

// The frame around every screen of one entity (the routes under entities/:entity, inside
// AppLayout): it loads the entity once, shows its name, description and tabs, and hands the entity
// to the screen (useEntity). An unknown entity shows a 'not found' state instead of the screen.
const route = useRoute()
const { application } = useApplication()

// AppLayout gives every application a fresh screen, so the token is fixed for the life of this one.
const appToken = application.value.Token

const { data: entity, error, loading, reload } = provideEntity(appToken, () => String(route.params.entity))

// Entity names are case-sensitive in the API: another spelling is 404 too.
const notFound = computed(() => error.value instanceof ApiError && error.value.status === 404)

// The tabs of an entity. One with `name` is a screen of the entity (the name of its route). One with
// `to` is a screen of the application opened at this entity (Data, Security).
const tabs = computed(() => {
  const current = entity.value

  if (!current) {
    return []
  }

  const all: { label: string; name?: string; to?: RouteLocationRaw }[] = [
    { label: 'Properties', name: 'app-entity-properties' },
    { label: 'Constraints', name: 'app-entity-constraints' },
    { label: 'Sorting', name: 'app-entity-sorting' },
    { label: 'Data', to: { name: 'app-data', params: { appToken, entity: current.Name } } },
    { label: 'Security', to: { name: 'app-security', params: { appToken }, query: { item: `Entity-${current.Name}` } } },
  ]

  return all
})

const tabClass =
  'inline-flex items-center rounded-md px-2.5 py-1 text-sm whitespace-nowrap transition-colors focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring'
const activeTabClass = 'bg-secondary font-medium text-secondary-foreground'
const inactiveTabClass = 'text-muted-foreground hover:bg-muted hover:text-foreground'
</script>

<template>
  <LoadingState v-if="loading" label="Loading the entity" />

  <StateMessage
    v-else-if="notFound"
    :icon="CompassIcon"
    title="Entity not found"
    as="h1"
    :description="`This application has no entity named '${route.params.entity}'. Names are case-sensitive.`"
  >
    <Button as-child>
      <RouterLink :to="{ name: 'app-entities', params: { appToken } }">Go to entities</RouterLink>
    </Button>
  </StateMessage>

  <ErrorState v-else-if="error" :error="error" @retry="reload" />

  <template v-else-if="entity">
    <RouterLink
      :to="{ name: 'app-entities', params: { appToken } }"
      class="mb-3 inline-flex items-center gap-1.5 rounded-sm text-sm text-muted-foreground underline-offset-4 hover:text-foreground hover:underline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring"
    >
      <ArrowLeftIcon class="size-3.5" aria-hidden="true" />
      Entities
    </RouterLink>

    <PageHeader :title="entity.Name" :description="entity.Description ?? undefined" class="wrap-anywhere">
      <Badge v-if="entity.IsSystem" variant="secondary">System entity</Badge>
    </PageHeader>

    <!--
      The tabs of the entity: pills, so they do not look like the section tabs of the application
      above them. They wrap on a narrow screen.
    -->
    <nav aria-label="Entity sections" class="mb-6">
      <ul class="flex flex-wrap gap-1">
        <li v-for="tab in tabs" :key="tab.label">
          <RouterLink
            v-if="tab.name"
            :to="{ name: tab.name, params: { appToken, entity: entity.Name } }"
            :class="[tabClass, tab.name === route.name ? activeTabClass : inactiveTabClass]"
            :aria-current="tab.name === route.name ? 'page' : undefined"
          >
            {{ tab.label }}
          </RouterLink>
          <RouterLink v-else-if="tab.to" :to="tab.to" :class="[tabClass, inactiveTabClass]">{{ tab.label }}</RouterLink>
        </li>
      </ul>
    </nav>

    <!-- The key gives another entity a fresh screen, so no screen has to watch the name. -->
    <RouterView :key="entity.Name" />
  </template>
</template>
