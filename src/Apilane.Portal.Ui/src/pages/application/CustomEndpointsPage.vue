<script setup lang="ts">
import { BracesIcon, PencilIcon, PlayIcon, PlusIcon, SearchIcon, Trash2Icon } from '@lucide/vue'
import { ref } from 'vue'
import ConfirmByNameDialog from '@/components/ConfirmByNameDialog.vue'
import ErrorState from '@/components/ErrorState.vue'
import LoadingState from '@/components/LoadingState.vue'
import PageHeader from '@/components/PageHeader.vue'
import StateMessage from '@/components/StateMessage.vue'
import CustomEndpointsHelp from '@/components/help/CustomEndpointsHelp.vue'
import { Button } from '@/components/ui/button'
import { useApplication } from '@/composables/useApplication'
import { useAsync } from '@/composables/useAsync'
import { useMutation } from '@/composables/useMutation'
import { api, unwrap } from '@/lib/api'
import type { Schemas } from '@/lib/api'
import * as toast from '@/lib/toast'
import CustomEndpointSearchDialog from './CustomEndpointSearchDialog.vue'

type Endpoint = Schemas['CustomEndpointResponse']

// The custom endpoints of one application: one line each, the SQL cut to one line. The search
// dialog shows the whole SQL of the matches.
const { application, reload: reloadApplication } = useApplication()

// AppLayout gives every application a fresh screen, so the token is fixed for the life of this one.
const appToken = application.value.Token

const { data, error, loading, reload } = useAsync(() =>
  unwrap(api.GET('/api/v1/applications/{appToken}/custom-endpoints', { params: { path: { appToken } } })),
)

const searchOpen = ref(false)

// `deleting` keeps its value after the dialog closes, so the text does not change while it fades out.
const deleteOpen = ref(false)
const deleting = ref<Endpoint>()

const remove = useMutation((id: number) =>
  unwrap(api.DELETE('/api/v1/applications/{appToken}/custom-endpoints/{id}', { params: { path: { appToken, id } } })),
)

function openDelete(endpoint: Endpoint): void {
  deleting.value = endpoint
  remove.reset()
  deleteOpen.value = true
}

async function submitDelete(): Promise<boolean> {
  if (!deleting.value || !(await remove.run(deleting.value.ID))) {
    return false
  }

  toast.success('Custom endpoint deleted.')
  void reload()
  // The number of custom endpoints is part of the application (the rename warnings of entities read it).
  void reloadApplication()
  return true
}
</script>

<template>
  <PageHeader title="Custom endpoints" description="SQL queries that the API server runs when their address is called.">
    <CustomEndpointsHelp />
    <Button v-if="data && data.Data.length > 0" variant="outline" size="icon" title="Search" @click="searchOpen = true">
      <SearchIcon />
      <span class="sr-only">Search custom endpoints</span>
    </Button>
    <Button as-child>
      <RouterLink :to="{ name: 'app-endpoint-create', params: { appToken } }">
        <PlusIcon />
        New endpoint
      </RouterLink>
    </Button>
  </PageHeader>

  <LoadingState v-if="loading" label="Loading custom endpoints" />

  <ErrorState v-else-if="error" :error="error" @retry="reload" />

  <StateMessage
    v-else-if="!data || data.Data.length === 0"
    :icon="BracesIcon"
    title="No custom endpoints yet"
    description="Create your first custom endpoint: a SQL query of your own, with numeric parameters, that clients call like any other endpoint."
  >
    <Button as-child>
      <RouterLink :to="{ name: 'app-endpoint-create', params: { appToken } }">
        <PlusIcon />
        New endpoint
      </RouterLink>
    </Button>
  </StateMessage>

  <ul v-else class="divide-y overflow-hidden rounded-lg border bg-card" aria-label="Custom endpoints">
    <li v-for="endpoint in data.Data" :key="endpoint.ID" class="flex items-center gap-3 px-4 py-2.5">
      <!-- min-w-0 lets the texts shrink and end in an ellipsis instead of pushing the buttons off a phone screen. -->
      <div class="min-w-0 flex-1">
        <p class="truncate text-sm font-medium" :title="endpoint.Name">{{ endpoint.Name }}</p>
        <p v-if="endpoint.Description" class="truncate text-xs text-muted-foreground" :title="endpoint.Description">
          {{ endpoint.Description }}
        </p>
        <!-- One line only: the search dialog shows the whole SQL. -->
        <p class="mt-0.5 truncate font-mono text-xs text-muted-foreground" :title="endpoint.Query">{{ endpoint.Query }}</p>
      </div>

      <div class="flex shrink-0 items-center gap-1">
        <Button
          as-child
          variant="outline"
          size="icon-sm"
          class="border-success/50 text-success hover:bg-success/10 hover:text-success dark:border-success/50 dark:hover:bg-success/10"
        >
          <a :href="endpoint.CallUrl" target="_blank" rel="noopener" title="Call endpoint">
            <PlayIcon />
            <span class="sr-only">Call {{ endpoint.Name }} (opens in a new tab)</span>
          </a>
        </Button>
        <Button as-child variant="ghost" size="icon-sm">
          <RouterLink :to="{ name: 'app-endpoint-edit', params: { appToken, id: endpoint.ID } }" title="Edit">
            <PencilIcon />
            <span class="sr-only">Edit {{ endpoint.Name }}</span>
          </RouterLink>
        </Button>
        <Button variant="ghost" size="icon-sm" title="Delete" @click="openDelete(endpoint)">
          <Trash2Icon />
          <span class="sr-only">Delete {{ endpoint.Name }}</span>
        </Button>
      </div>
    </li>
  </ul>

  <CustomEndpointSearchDialog v-if="data" v-model:open="searchOpen" :endpoints="data.Data" :app-token="appToken" />

  <ConfirmByNameDialog
    v-if="deleting"
    v-model:open="deleteOpen"
    :title="`Delete custom endpoint ${deleting.Name}?`"
    description="This cannot be undone."
    :name="deleting.Name"
    :action="submitDelete"
    :error="remove.error.value?.message"
  />
</template>
