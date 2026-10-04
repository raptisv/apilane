<script setup lang="ts">
import { ChevronRightIcon, CompassIcon, ExternalLinkIcon, InfoIcon } from '@lucide/vue'
import { computed, ref, useTemplateRef, watch } from 'vue'
import { useRoute } from 'vue-router'
import ApplicationInfoDialog from '@/components/ApplicationInfoDialog.vue'
import ErrorState from '@/components/ErrorState.vue'
import ForbiddenState from '@/components/ForbiddenState.vue'
import LoadingState from '@/components/LoadingState.vue'
import ServerStatusDot from '@/components/ServerStatusDot.vue'
import StateMessage from '@/components/StateMessage.vue'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { provideApplication } from '@/composables/useApplication'
import { ApiError } from '@/lib/api'
import { swaggerUrl } from '@/lib/apiServer'

// The frame around every screen of one application (the routes under apps/:appToken): it loads
// the application once, shows the breadcrumb and the section navigation, and hands the
// application to the screen (useApplication). An unknown or inaccessible application shows a
// 'not found' state instead of the screen.
const route = useRoute()

const { data: application, error, loading, reload } = provideApplication(() => String(route.params.appToken))

// The API answers 404 both for a token that does not exist and for an application of someone else.
const notFound = computed(() => error.value instanceof ApiError && error.value.status === 404)

// The sections of an application. `segment` is both the last part of the route name
// ('app-entities') and the part of the address after the token (/apps/<token>/entities/...), which
// decides whether the tab is the current one. `name` is for the one screen whose route cannot be
// named after its segment ('app-import' is the page that creates an application from an export).
const sections = computed(() => {
  const app = application.value

  if (!app) {
    return []
  }

  const all: { label: string; segment: string; name?: string; ownerOnly?: boolean }[] = [
    { label: 'Entities', segment: 'entities' },
    { label: 'Data', segment: 'data' },
    { label: 'Security', segment: 'security' },
    { label: 'Custom endpoints', segment: 'endpoints' },
    { label: 'Email', segment: 'email' },
    { label: 'Reports', segment: 'reports' },
    { label: 'Sharing', segment: 'sharing', ownerOnly: true },
    { label: 'Import', segment: 'import', name: 'app-schema-import' },
    { label: 'Audit log', segment: 'audit-log' },
    { label: 'Settings', segment: 'settings' },
  ]

  return all.filter((section) => !section.ownerOnly || app.IsOwner)
})

// The one place that enforces meta.requiresOwner: a collaborator gets the Forbidden state instead of
// the screen, and the address stays. The API refuses the same calls on its own.
const forbidden = computed(() => route.meta.requiresOwner === true && application.value?.IsOwner !== true)

// '/apps/<token>/entities/...' -> 'entities'. Lower case: the path keeps the case that was typed.
const currentSegment = computed(() => route.path.split('/')[3]?.toLowerCase())

const infoOpen = ref(false)

// On a phone the tabs scroll sideways: start with the current one in view.
const nav = useTemplateRef('nav')

watch(nav, (el) => el?.querySelector('[aria-current="page"]')?.scrollIntoView({ block: 'nearest', inline: 'nearest' }))

const crumbLinkClass =
  'rounded-sm underline-offset-4 hover:text-foreground hover:underline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring'
const tabClass =
  'inline-flex items-center gap-1 border-b-2 px-2.5 py-2 text-sm whitespace-nowrap transition-colors focus-visible:outline-2 focus-visible:-outline-offset-2 focus-visible:outline-ring'
const activeTabClass = 'border-primary font-medium text-foreground'
const inactiveTabClass = 'border-transparent text-muted-foreground hover:text-foreground'
</script>

<template>
  <LoadingState v-if="loading" label="Loading the application" />

  <StateMessage
    v-else-if="notFound"
    :icon="CompassIcon"
    as="h1"
    title="Application not found"
    description="There is no application at this address, or it is not shared with you."
  >
    <Button as-child>
      <RouterLink :to="{ name: 'apps' }">Go to applications</RouterLink>
    </Button>
  </StateMessage>

  <ErrorState v-else-if="error" :error="error" @retry="reload" />

  <template v-else-if="application">
    <div class="mb-3 flex flex-wrap items-center justify-between gap-x-4 gap-y-2">
      <nav aria-label="Breadcrumb" class="min-w-0">
        <ol class="flex flex-wrap items-center gap-x-1.5 gap-y-1 text-sm text-muted-foreground">
          <li>
            <RouterLink :to="{ name: 'apps' }" :class="crumbLinkClass">Applications</RouterLink>
          </li>
          <li aria-hidden="true"><ChevronRightIcon class="size-3.5" /></li>
          <li class="flex min-w-0 flex-wrap items-center gap-x-2.5 gap-y-1">
            <RouterLink
              :to="{ name: 'app-entities', params: { appToken: application.Token } }"
              :class="crumbLinkClass"
              class="font-medium wrap-anywhere text-foreground"
            >
              {{ application.Name }}
            </RouterLink>
            <Badge v-if="!application.Online" variant="destructive">Offline</Badge>
            <span class="flex items-center gap-1.5 text-xs" :title="application.Server.ServerUrl">
              <ServerStatusDot :server-url="application.Server.ServerUrl" />
              <span class="sr-only">Server</span>
              <span class="wrap-anywhere">{{ application.Server.Name }}</span>
            </span>
          </li>
          <li aria-hidden="true"><ChevronRightIcon class="size-3.5" /></li>
          <li aria-current="page">{{ route.meta.title }}</li>
        </ol>
      </nav>

      <div class="flex shrink-0 items-center gap-2">
        <Button variant="outline" size="sm" @click="infoOpen = true">
          <InfoIcon />
          Info
        </Button>
        <Button as-child variant="outline" size="sm">
          <a :href="swaggerUrl(application.Server.ServerUrl, application.Token, application.Name)" target="_blank" rel="noopener">
            <ExternalLinkIcon />
            API
            <span class="sr-only">(Swagger, opens in a new tab)</span>
          </a>
        </Button>
      </div>
    </div>

    <!--
      Tabs. On a phone they are one row that scrolls sideways by touch, out to the screen edges and
      without a scrollbar. From `sm` up they wrap onto a second row when they do not fit.
      `relative` keeps the sr-only texts (position: absolute) inside the scrolling row; without it
      they widen the page.
    -->
    <nav
      ref="nav"
      aria-label="Application sections"
      class="relative -mx-4 mb-6 overflow-x-auto border-b px-4 [scrollbar-width:none] sm:mx-0 sm:overflow-visible sm:px-0"
    >
      <ul class="flex w-max sm:w-auto sm:flex-wrap">
        <li v-for="section in sections" :key="section.label">
          <RouterLink
            :to="{ name: section.name ?? `app-${section.segment}`, params: { appToken: application.Token } }"
            :class="[tabClass, section.segment === currentSegment ? activeTabClass : inactiveTabClass]"
            :aria-current="section.segment === currentSegment ? 'page' : undefined"
          >
            {{ section.label }}
          </RouterLink>
        </li>
      </ul>
    </nav>

    <!-- The key gives another application a fresh screen, so no screen has to watch the token. -->
    <ForbiddenState v-if="forbidden" description="Only the owner of this application can open it." />
    <RouterView v-else :key="application.Token" />

    <ApplicationInfoDialog
      v-model:open="infoOpen"
      :name="application.Name"
      :token="application.Token"
      :server-url="application.Server.ServerUrl"
    />
  </template>
</template>
