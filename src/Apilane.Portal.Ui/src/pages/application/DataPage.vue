<script setup lang="ts">
import { TablePropertiesIcon } from '@lucide/vue'
import { computed, watch } from 'vue'
import { useRoute } from 'vue-router'
import ApplicationDataBrowser from '@/components/data/ApplicationDataBrowser.vue'
import ErrorState from '@/components/ErrorState.vue'
import LoadingState from '@/components/LoadingState.vue'
import PageHeader from '@/components/PageHeader.vue'
import StateMessage from '@/components/StateMessage.vue'
import { Button } from '@/components/ui/button'
import { useApplication } from '@/composables/useApplication'
import { useAsync } from '@/composables/useAsync'
import { api, unwrap } from '@/lib/api'
import { setTitle } from '@/router'

// The data browser of an application (/apps/:appToken/data/:entity): the entity switcher and the
// records of one entity, both drawn by ApplicationDataBrowser. /apps/:appToken/data alone opens the
// entity of ?entity=Name (the address of the classic data browser) or else the first custom entity.
const route = useRoute()
const { application } = useApplication()

// AppLayout gives every application a fresh screen, so the token is fixed for the life of this one.
const appToken = application.value.Token

const { data, error, loading, reload } = useAsync(() =>
  unwrap(api.GET('/api/v1/applications/{appToken}/entities', { params: { path: { appToken }, query: { IncludeProperties: true } } })),
)

const entities = computed(() => data.value?.Data ?? [])

// The entity of the address, for the title, once it is known to exist.
const shown = computed(() => entities.value.find((entity) => entity.Name === route.params.entity)?.Name)

// The browser tab names the entity too, so tabs open on different entities can be told apart. The
// router puts the plain title back after every navigation (a new ?page= or ?sort= is one), so it is
// set again each time the address changes.
watch([shown, () => route.fullPath], () => setTitle(shown.value ? `Data: ${shown.value}` : route.meta.title), { immediate: true })

function entityRoute(entity: string) {
  return { name: 'app-data', params: { appToken, entity } }
}
</script>

<template>
  <PageHeader
    :title="shown ? `Data: ${shown}` : 'Data'"
    description="Browse, filter and change the records of each entity."
    class="wrap-anywhere"
  />

  <LoadingState v-if="loading" label="Loading entities" />

  <ErrorState v-else-if="error" :error="error" @retry="reload" />

  <StateMessage
    v-else-if="entities.length === 0"
    :icon="TablePropertiesIcon"
    title="No entities yet"
    description="Add an entity before adding records."
  >
    <Button as-child>
      <RouterLink :to="{ name: 'app-entities', params: { appToken } }">Go to entities</RouterLink>
    </Button>
  </StateMessage>

  <ApplicationDataBrowser v-else :application="application" :entities="entities" :to="entityRoute" />
</template>
