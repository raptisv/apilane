<script setup lang="ts">
import { CompassIcon } from '@lucide/vue'
import { computed, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import type { RouteLocationRaw } from 'vue-router'
import StateMessage from '@/components/StateMessage.vue'
import { dataEntityOrder, initialDataEntity } from '@/lib/entities'
import type { Entity } from '@/lib/entities'
import type { DataApplication } from '@/lib/records'
import EntityDataBrowser from './EntityDataBrowser.vue'
import EntitySwitcher from './EntitySwitcher.vue'

// The data browser of one application: the entity switcher and the records of the entity named by
// the route parameter `entity`. With no entity in the address it opens the one of ?entity=<name>
// (the address of the classic data browser) or else the first custom entity.
//
// Use it on a screen whose route ends in ':entity?'. The screen loads the entities, shows its own
// loading, error and 'no entities' states, and mounts this once there is at least one entity:
// pages/application/DataPage.vue (the application's own) and pages/admin/ApplicationDataPage.vue
// (the administrator's, of any application).
const props = defineProps<{
  application: DataApplication
  /** The entities with their Properties, by name, as the API sends them. */
  entities: readonly Entity[]
  /** The route of one entity on the screen that hosts this. */
  to: (entity: string) => RouteLocationRaw
  /** See EntityDataBrowser: the application is someone else's. */
  foreign?: boolean
}>()

const route = useRoute()
const router = useRouter()

// Custom entities first, then the system ones, each by name.
const ordered = computed(() => dataEntityOrder(props.entities))

const entityName = computed(() => (typeof route.params.entity === 'string' ? route.params.entity : undefined))

// Names are case-sensitive, as everywhere in the Portal.
const current = computed(() => ordered.value.find((entity) => entity.Name === entityName.value))

// No entity in the address: open the one of ?entity=, else the first.
watch(
  [ordered, entityName],
  ([list, name]) => {
    if (name !== undefined) {
      return
    }

    const target = initialDataEntity(list, typeof route.query.entity === 'string' ? route.query.entity : undefined)

    if (target) {
      void router.replace(props.to(target.Name))
    }
  },
  { immediate: true },
)
</script>

<template>
  <div class="mb-4">
    <EntitySwitcher :entities="ordered" :current="current?.Name" :to="to" />
  </div>

  <!-- The key gives another entity a fresh grid: its own page, sort and filters. -->
  <EntityDataBrowser v-if="current" :key="current.Name" :application="application" :entity="current" :foreign="foreign" />

  <StateMessage
    v-else-if="entityName !== undefined"
    :icon="CompassIcon"
    title="Entity not found"
    :description="`This application has no entity named '${entityName}'. Names are case-sensitive.`"
  />
</template>
