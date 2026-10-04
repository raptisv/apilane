<script setup lang="ts">
import { ChevronRightIcon, CompassIcon, TablePropertiesIcon } from '@lucide/vue'
import { computed } from 'vue'
import { useRoute } from 'vue-router'
import ApplicationDataBrowser from '@/components/data/ApplicationDataBrowser.vue'
import ErrorState from '@/components/ErrorState.vue'
import LoadingState from '@/components/LoadingState.vue'
import PageHeader from '@/components/PageHeader.vue'
import StateMessage from '@/components/StateMessage.vue'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { useApplications } from '@/composables/useApplications'
import { useAsync } from '@/composables/useAsync'
import { api, ApiError, unwrap } from '@/lib/api'

// The administrator's data browser (/admin/applications/:appToken/data/:entity): the records of
// ANY application of the instance, whoever owns it. The application and its entities come from the
// admin endpoint; the records are read and written on the API server, which gives an administrator
// the rights of the owner. The screens under /apps/:appToken stay for owners and collaborators.
const route = useRoute()

const appToken = computed(() => String(route.params.appToken))

const { data, error, loading, reload } = useAsync(
  () => unwrap(api.GET('/api/v1/admin/applications/{appToken}', { params: { path: { appToken: appToken.value } } })),
  { watch: appToken },
)

const notFound = computed(() => error.value instanceof ApiError && error.value.status === 404)

// The entity of the address, for the title, once it is known to exist.
const shown = computed(() => data.value?.Entities.find((entity) => entity.Name === route.params.entity)?.Name)

// Someone else's application, unless the administrator owns it or it is shared with them (and
// until that list has loaded): the data browser then leaves out links to its screens under /apps.
const { applications: own } = useApplications()
const foreign = computed(() => !own.value?.some((application) => application.Token === appToken.value))

function entityRoute(entity: string) {
  return { name: 'admin-application-data', params: { appToken: appToken.value, entity } }
}

const crumbLinkClass =
  'rounded-sm underline-offset-4 hover:text-foreground hover:underline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring'
</script>

<template>
  <LoadingState v-if="loading" label="Loading the application" />

  <StateMessage
    v-else-if="notFound"
    :icon="CompassIcon"
    as="h1"
    title="Application not found"
    description="This instance has no application with this token. Tokens are case-sensitive."
  >
    <Button as-child>
      <RouterLink :to="{ name: 'admin-applications' }">Go to applications</RouterLink>
    </Button>
  </StateMessage>

  <ErrorState v-else-if="error" :error="error" @retry="reload" />

  <template v-else-if="data">
    <nav aria-label="Breadcrumb" class="mb-3">
      <ol class="flex flex-wrap items-center gap-x-1.5 gap-y-1 text-sm text-muted-foreground">
        <li>Instance</li>
        <li aria-hidden="true"><ChevronRightIcon class="size-3.5" /></li>
        <li>
          <RouterLink :to="{ name: 'admin-applications' }" :class="crumbLinkClass">Applications</RouterLink>
        </li>
        <li aria-hidden="true"><ChevronRightIcon class="size-3.5" /></li>
        <li class="flex min-w-0 flex-wrap items-center gap-x-2.5 gap-y-1">
          <span class="font-medium wrap-anywhere text-foreground">{{ data.Application.Name }}</span>
          <Badge v-if="!data.Application.Online" variant="destructive">Offline</Badge>
        </li>
        <li aria-hidden="true"><ChevronRightIcon class="size-3.5" /></li>
        <li aria-current="page">Data</li>
      </ol>
    </nav>

    <PageHeader
      :title="shown ? `Data: ${shown}` : 'Data'"
      description="Browse, filter and change the records of each entity. An administrator can do this in any application of the instance."
      class="wrap-anywhere"
    />

    <StateMessage
      v-if="data.Entities.length === 0"
      :icon="TablePropertiesIcon"
      title="No entities"
      description="This application has no entities, so it has no records to browse."
    />

    <!-- The key gives another application a fresh browser. -->
    <ApplicationDataBrowser
      v-else
      :key="data.Application.Token"
      :application="data.Application"
      :entities="data.Entities"
      :to="entityRoute"
      :foreign="foreign"
    />
  </template>
</template>
