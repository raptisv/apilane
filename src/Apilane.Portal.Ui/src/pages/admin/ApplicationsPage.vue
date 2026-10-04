<script setup lang="ts">
import {
  AppWindowIcon,
  ChevronDownIcon,
  DatabaseIcon,
  EllipsisVerticalIcon,
  LayoutGridIcon,
  Loader2Icon,
  RefreshCwIcon,
  SearchIcon,
  SearchXIcon,
} from '@lucide/vue'
import { computed, ref } from 'vue'
import ErrorState from '@/components/ErrorState.vue'
import LoadingState from '@/components/LoadingState.vue'
import PageHeader from '@/components/PageHeader.vue'
import StateMessage from '@/components/StateMessage.vue'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuSeparator,
  DropdownMenuTrigger,
} from '@/components/ui/dropdown-menu'
import { Input } from '@/components/ui/input'
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@/components/ui/table'
import { useApplications } from '@/composables/useApplications'
import { useAsync } from '@/composables/useAsync'
import { useMutation } from '@/composables/useMutation'
import { applicationDetails, countText, searchApplications } from '@/lib/adminApplications'
import type { AdminApplication } from '@/lib/adminApplications'
import { api, unwrap } from '@/lib/api'
import { databaseTypeLabel } from '@/lib/applications'
import * as toast from '@/lib/toast'

// Every application of the instance, whoever owns it, in the order they were created. The table
// keeps the columns an administrator looks for; the other settings open under the row. A secret (connection string, mail password) is never sent: only whether one is stored.
const { data, error, loading, reload } = useAsync(() => unwrap(api.GET('/api/v1/admin/applications')))

const all = computed(() => data.value?.Data ?? [])

const search = ref('')
const shown = computed(() => searchApplications(all.value, search.value))

// The administrator's own applications (owned or shared). Only those open under /apps: there the
// Admin role gives nothing, so 'Open application' is offered for them alone.
const { applications: own } = useApplications()
const ownTokens = computed(() => new Set(own.value?.map((application) => application.Token)))

// The IDs of the rows whose details are open.
const open = ref(new Set<number>())

function toggle(id: number): void {
  if (!open.value.delete(id)) {
    open.value.add(id)
  }
}

function dataRoute(application: AdminApplication) {
  return { name: 'admin-application-data', params: { appToken: application.Token } }
}

// Makes the API server read the application again. This endpoint accepts an administrator for any application.
const clearCache = useMutation((appToken: string) =>
  unwrap(api.POST('/api/v1/applications/{appToken}/cache-reset', { params: { path: { appToken } } })),
)

// The token of the application whose cache is being cleared: its menu item shows the spinner.
const clearing = ref<string>()

async function clear(application: AdminApplication): Promise<void> {
  // The busy item stays enabled and its menu stays open: a second select does nothing.
  if (clearCache.pending.value) {
    return
  }

  clearing.value = application.Token
  const cleared = await clearCache.run(application.Token)
  clearing.value = undefined

  if (cleared) {
    toast.success(`Cache of ${application.Name} cleared.`)
  } else {
    // The API server refused, failed or could not be reached: its own message, with the trace id.
    toast.error(clearCache.error.value)
  }
}
</script>

<template>
  <PageHeader title="Applications" description="Every application of this instance, whoever owns it." />

  <LoadingState v-if="loading" label="Loading applications" />

  <ErrorState v-else-if="error" :error="error" @retry="reload" />

  <StateMessage
    v-else-if="all.length === 0"
    :icon="LayoutGridIcon"
    title="No applications yet"
    description="The applications that users of this instance create are listed here."
  />

  <template v-else>
    <div class="mb-4 flex flex-wrap items-center justify-between gap-x-4 gap-y-2">
      <div class="relative w-full sm:max-w-xs">
        <SearchIcon class="pointer-events-none absolute top-1/2 left-2.5 size-4 -translate-y-1/2 text-muted-foreground" />
        <Input
          v-model="search"
          type="search"
          class="pl-8"
          placeholder="Search by name, token or owner"
          aria-label="Search applications by name, token or owner email"
        />
      </div>
      <p role="status" class="text-sm text-muted-foreground">{{ countText(shown.length, all.length) }}</p>
    </div>

    <StateMessage
      v-if="shown.length === 0"
      :icon="SearchXIcon"
      title="No application matches"
      :description="`No application has '${search.trim()}' in its name, token or owner email.`"
    >
      <Button variant="outline" @click="search = ''">Clear search</Button>
    </StateMessage>

    <div v-else class="overflow-hidden rounded-lg border bg-card">
      <Table>
        <TableHeader>
          <TableRow>
            <TableHead class="pl-4">Application</TableHead>
            <TableHead class="hidden md:table-cell">Owner</TableHead>
            <TableHead class="hidden md:table-cell">Server</TableHead>
            <TableHead class="hidden md:table-cell">Database</TableHead>
            <TableHead class="hidden md:table-cell">Status</TableHead>
            <TableHead class="pr-4"><span class="sr-only">Actions</span></TableHead>
          </TableRow>
        </TableHeader>
        <TableBody>
          <template v-for="application in shown" :key="application.ID">
            <TableRow>
              <TableCell class="pl-4 align-top whitespace-normal">
                <RouterLink
                  :to="dataRoute(application)"
                  class="rounded-sm font-medium wrap-anywhere text-link underline-offset-4 hover:underline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring"
                >
                  {{ application.Name }}
                  <span class="sr-only">(opens the data browser)</span>
                </RouterLink>
                <p class="mt-0.5 font-mono text-xs break-all text-muted-foreground">
                  <span class="sr-only">Token </span>{{ application.Token }}
                </p>

                <!-- On a narrow screen the other columns move under the name, so the table fits the screen. -->
                <div class="mt-1.5 flex flex-col items-start gap-1 text-xs text-muted-foreground md:hidden">
                  <p v-if="application.OwnerEmail" class="wrap-anywhere">
                    <span class="sr-only">Owner </span>{{ application.OwnerEmail }}
                  </p>
                  <p class="wrap-anywhere">
                    <span class="sr-only">Server </span>{{ application.Server.Name }} ·
                    {{ databaseTypeLabel(application.DatabaseType) }}
                  </p>
                  <Badge v-if="application.Online" variant="outline" class="border-success/40 text-success">Online</Badge>
                  <Badge v-else variant="destructive">Offline</Badge>
                </div>
              </TableCell>

              <TableCell class="hidden align-top break-all whitespace-normal text-muted-foreground md:table-cell">
                {{ application.OwnerEmail }}
              </TableCell>

              <TableCell class="hidden align-top whitespace-normal md:table-cell">
                <span class="wrap-anywhere" :title="application.Server.ServerUrl">{{ application.Server.Name }}</span>
              </TableCell>

              <TableCell class="hidden align-top md:table-cell">{{ databaseTypeLabel(application.DatabaseType) }}</TableCell>

              <TableCell class="hidden align-top md:table-cell">
                <Badge v-if="application.Online" variant="outline" class="border-success/40 text-success">Online</Badge>
                <Badge v-else variant="destructive">Offline</Badge>
              </TableCell>

              <TableCell class="w-px pr-4 align-top">
                <div class="flex justify-end gap-1">
                  <Button
                    variant="ghost"
                    size="icon-sm"
                    title="Details"
                    :aria-expanded="open.has(application.ID)"
                    @click="toggle(application.ID)"
                  >
                    <ChevronDownIcon class="transition-transform" :class="open.has(application.ID) && 'rotate-180'" />
                    <span class="sr-only">Details of {{ application.Name }}</span>
                  </Button>

                  <DropdownMenu>
                    <DropdownMenuTrigger as-child>
                      <Button variant="ghost" size="icon-sm">
                        <EllipsisVerticalIcon />
                        <span class="sr-only">Actions for {{ application.Name }}</span>
                      </Button>
                    </DropdownMenuTrigger>

                    <DropdownMenuContent align="end" class="w-52">
                      <DropdownMenuItem as-child>
                        <RouterLink :to="dataRoute(application)"><DatabaseIcon />Open data browser</RouterLink>
                      </DropdownMenuItem>
                      <DropdownMenuItem v-if="ownTokens.has(application.Token)" as-child>
                        <RouterLink :to="{ name: 'app-entities', params: { appToken: application.Token } }">
                          <AppWindowIcon />Open application
                        </RouterLink>
                      </DropdownMenuItem>

                      <DropdownMenuSeparator />

                      <!-- The menu stays open while the call runs, so the busy state is visible. -->
                      <DropdownMenuItem
                        :aria-busy="clearing === application.Token"
                        :disabled="clearCache.pending.value && clearing !== application.Token"
                        @select.prevent="clear(application)"
                      >
                        <Loader2Icon v-if="clearing === application.Token" class="animate-spin" />
                        <RefreshCwIcon v-else />
                        {{ clearing === application.Token ? 'Clearing…' : 'Clear cache' }}
                      </DropdownMenuItem>
                    </DropdownMenuContent>
                  </DropdownMenu>
                </div>
              </TableCell>
            </TableRow>

            <TableRow v-if="open.has(application.ID)" class="hover:bg-transparent">
              <TableCell colspan="6" class="bg-muted/30 px-4 py-3 whitespace-normal">
                <dl class="grid gap-x-6 gap-y-3 sm:grid-cols-2 lg:grid-cols-3">
                  <div v-for="detail in applicationDetails(application)" :key="detail.label" class="min-w-0">
                    <dt class="text-xs text-muted-foreground">{{ detail.label }}</dt>
                    <dd class="mt-0.5 wrap-anywhere">
                      <Badge v-if="detail.stored !== undefined" :variant="detail.stored ? 'secondary' : 'outline'">
                        {{ detail.stored ? 'Set' : 'Not set' }}
                      </Badge>
                      <template v-else-if="detail.value">{{ detail.value }}</template>
                      <span v-else class="text-muted-foreground">Not set</span>
                    </dd>
                  </div>
                </dl>
              </TableCell>
            </TableRow>
          </template>
        </TableBody>
      </Table>
    </div>
  </template>
</template>
