<script setup lang="ts">
import { AppWindowIcon, PlusIcon, SearchIcon, SearchXIcon, UploadIcon } from '@lucide/vue'
import { computed, onMounted, ref, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import ApplicationInfoDialog from '@/components/ApplicationInfoDialog.vue'
import ApplicationStatusDialog from '@/components/ApplicationStatusDialog.vue'
import DeleteApplicationDialog from '@/components/DeleteApplicationDialog.vue'
import ErrorState from '@/components/ErrorState.vue'
import PageHeader from '@/components/PageHeader.vue'
import RebuildApplicationDialog from '@/components/RebuildApplicationDialog.vue'
import ServerStatusDot from '@/components/ServerStatusDot.vue'
import StateMessage from '@/components/StateMessage.vue'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { Skeleton } from '@/components/ui/skeleton'
import { useApplications } from '@/composables/useApplications'
import { healthUrl } from '@/composables/useServerHealth'
import { filterByName, groupByServer } from '@/lib/applications'
import type { Application } from '@/lib/applications'
import * as toast from '@/lib/toast'
import ApplicationCard from './ApplicationCard.vue'
import CompareApplicationsDialog from './CompareApplicationsDialog.vue'

const route = useRoute()
const router = useRouter()

// The list is shared with the sidebar switcher, which has usually loaded it already.
const { applications, error, loading, refreshing, reload } = useApplications()

// Coming back to this page shows the list as it is now, not as it was when the app started. A load
// that is already running (started by a delete on the settings screen) is enough.
onMounted(() => {
  if (!loading.value && !refreshing.value) {
    void reload()
  }
})

// The search box appears only when there is enough to search through.
const searchFrom = 7
const search = ref('')
const searchable = computed(() => (applications.value?.length ?? 0) >= searchFrom)
// Every card stays mounted: a search only hides cards, so their size on disk and the server's health dot are not loaded again.
const groups = computed(() => groupByServer(applications.value ?? []))
const matching = computed(
  () => new Set(filterByName(applications.value ?? [], searchable.value ? search.value : '').map((application) => application.Token)),
)

// The info dialog is open while the address carries ?info=<application token>.
const infoToken = computed(() => (typeof route.query.info === 'string' ? route.query.info : undefined))
const infoApplication = computed(() => applications.value?.find((application) => application.Token === infoToken.value))
// Keeps its value after the dialog closes, so the text does not change while it fades out.
const shownApplication = ref<Application>()

function setInfo(token: string | undefined): void {
  const query = { ...route.query }

  if (token) {
    query.info = token
  } else {
    delete query.info
  }

  void router.replace({ query })
}

const infoOpen = computed({
  get: () => infoApplication.value !== undefined,
  set: (open) => {
    if (!open) {
      setInfo(undefined)
    }
  },
})

// The status, rebuild and delete dialogs of a card. Each reloads the list itself where it needs to.
const acting = ref<Application>()
const statusOpen = ref(false)
const rebuildOpen = ref(false)
const deleteOpen = ref(false)

function openAction(action: 'status' | 'rebuild' | 'delete', application: Application): void {
  acting.value = application
  statusOpen.value = action === 'status'
  rebuildOpen.value = action === 'rebuild'
  deleteOpen.value = action === 'delete'
}

// A rebuild drops the data, so the card's size on disk is stale: a new key remounts that one card,
// which loads its size again.
const rebuilt = ref<Record<string, number>>({})

function onRebuilt(token: string): void {
  rebuilt.value[token] = (rebuilt.value[token] ?? 0) + 1
}

watch(
  [infoToken, applications],
  () => {
    if (infoApplication.value) {
      shownApplication.value = infoApplication.value
    } else if (infoToken.value && applications.value) {
      // A link to an application the user cannot see (any more).
      toast.error(new Error('Application not found.'))
      setInfo(undefined)
    }
  },
  { immediate: true },
)

// The compare dialog is open while the address carries ?compare=<application token>. The application
// it is compared with, picked in the dialog, is &with=<application token>.
const compareToken = computed(() => (typeof route.query.compare === 'string' ? route.query.compare : undefined))
const compareApplication = computed(() => applications.value?.find((application) => application.Token === compareToken.value))
// Both keep their value after the dialog closes, so it does not change while it fades out.
const comparedApplication = ref<Application>()
const comparedWith = ref<string>()

function setCompare(token: string | undefined, other?: string): void {
  const query = { ...route.query }

  delete query.compare
  delete query.with

  if (token) {
    query.compare = token

    if (other) {
      query.with = other
    }
  }

  void router.replace({ query })
}

const compareOpen = computed({
  get: () => compareApplication.value !== undefined,
  set: (open) => {
    if (!open) {
      setCompare(undefined)
    }
  },
})

watch(
  [compareToken, () => route.query.with, applications],
  () => {
    if (compareApplication.value) {
      comparedApplication.value = compareApplication.value
      comparedWith.value = typeof route.query.with === 'string' ? route.query.with : undefined
    } else if (compareToken.value && applications.value) {
      // A link to an application the user cannot see (any more).
      toast.error(new Error('Application not found.'))
      setCompare(undefined)
    }
  },
  { immediate: true },
)
</script>

<template>
  <PageHeader title="Applications" description="Your applications and the ones shared with you.">
    <Button as-child variant="outline">
      <RouterLink :to="{ name: 'app-import' }">
        <UploadIcon />
        <span>Import<span class="max-sm:sr-only"> application</span></span>
      </RouterLink>
    </Button>
    <Button as-child>
      <RouterLink :to="{ name: 'app-create' }">
        <PlusIcon />
        New application
      </RouterLink>
    </Button>
  </PageHeader>

  <div v-if="loading" role="status">
    <span class="sr-only">Loading applications</span>
    <Skeleton class="mb-3 h-5 w-40" />
    <div class="grid gap-4 sm:grid-cols-2 xl:grid-cols-3">
      <Skeleton v-for="n in 3" :key="n" class="h-44 w-full" />
    </div>
  </div>

  <ErrorState v-else-if="error" :error="error" @retry="reload" />

  <StateMessage
    v-else-if="!applications || applications.length === 0"
    :icon="AppWindowIcon"
    title="No applications yet"
    description="Create your first application, or import one from an export."
  >
    <Button as-child>
      <RouterLink :to="{ name: 'app-create' }">
        <PlusIcon />
        New application
      </RouterLink>
    </Button>
    <Button as-child variant="outline">
      <RouterLink :to="{ name: 'app-import' }">
        <UploadIcon />
        Import application
      </RouterLink>
    </Button>
  </StateMessage>

  <template v-else>
    <div v-if="searchable" class="relative mb-6 sm:max-w-xs">
      <SearchIcon class="pointer-events-none absolute top-1/2 left-2.5 size-4 -translate-y-1/2 text-muted-foreground" />
      <Input v-model="search" type="search" class="pl-8" placeholder="Search by name" aria-label="Search applications by name" />
    </div>

    <StateMessage
      v-if="matching.size === 0"
      :icon="SearchXIcon"
      title="No application matches"
      :description="`No application has '${search.trim()}' in its name.`"
    >
      <Button variant="outline" @click="search = ''">Clear search</Button>
    </StateMessage>

    <!-- gap, not margins: it skips the sections a search hides. -->
    <div class="flex flex-col gap-8">
      <section
        v-for="group in groups"
        v-show="group.applications.some((application) => matching.has(application.Token))"
        :key="group.server.ID"
        :aria-labelledby="`server-${group.server.ID}`"
      >
        <div class="mb-3 flex flex-wrap items-center gap-x-2.5 gap-y-1">
          <ServerStatusDot :server-url="group.server.ServerUrl" />
          <h2 :id="`server-${group.server.ID}`" class="text-sm font-semibold">
            <a
              :href="healthUrl(group.server.ServerUrl)"
              target="_blank"
              rel="noopener"
              class="rounded-sm wrap-anywhere underline-offset-4 hover:underline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring"
            >
              {{ group.server.Name }}
              <span class="sr-only">(opens the health check in a new tab)</span>
            </a>
          </h2>
          <span class="font-mono text-xs break-all text-muted-foreground">{{ group.server.ServerUrl }}</span>
        </div>

        <div class="grid gap-4 sm:grid-cols-2 xl:grid-cols-3">
          <ApplicationCard
            v-for="application in group.applications"
            v-show="matching.has(application.Token)"
            :key="`${application.Token}-${rebuilt[application.Token] ?? 0}`"
            :application="application"
            @info="setInfo(application.Token)"
            @compare="setCompare(application.Token)"
            @status="openAction('status', application)"
            @rebuild="openAction('rebuild', application)"
            @delete="openAction('delete', application)"
          />
        </div>
      </section>
    </div>
  </template>

  <ApplicationInfoDialog
    v-if="shownApplication"
    v-model:open="infoOpen"
    :name="shownApplication.Name"
    :token="shownApplication.Token"
    :server-url="shownApplication.Server.ServerUrl"
  />

  <template v-if="acting">
    <ApplicationStatusDialog v-model:open="statusOpen" :application="acting" />
    <RebuildApplicationDialog v-model:open="rebuildOpen" :application="acting" @done="onRebuilt(acting.Token)" />
    <DeleteApplicationDialog v-model:open="deleteOpen" :application="acting" />
  </template>

  <CompareApplicationsDialog
    v-if="comparedApplication"
    v-model:open="compareOpen"
    :source="comparedApplication"
    :applications="applications ?? []"
    :target="comparedWith"
    @update:target="(token) => setCompare(comparedApplication?.Token, token)"
  />
</template>
