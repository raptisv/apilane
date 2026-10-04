<script setup lang="ts">
import { CheckIcon, ChevronsUpDownIcon } from '@lucide/vue'
import { computed } from 'vue'
import { useRoute } from 'vue-router'
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuLabel,
  DropdownMenuSeparator,
  DropdownMenuTrigger,
} from '@/components/ui/dropdown-menu'
import { useApplications } from '@/composables/useApplications'
import { groupByServer } from '@/lib/applications'

// The quick way from anywhere to one application: a menu of the user's applications grouped by
// server, under 'Applications' in the sidebar. It shares its list with the applications page
// (useApplications). A user without applications sees nothing here. On a screen of one
// application (/apps/:appToken/...) it shows that application's name and marks it in the menu.
const { applications, error, reload } = useApplications()
const route = useRoute()

const groups = computed(() => groupByServer(applications.value ?? []))
// Only on the screens of one application: the admin data browser (/admin/applications/:appToken/data) has an :appToken too.
const current = computed(() =>
  route.path.startsWith('/apps/')
    ? applications.value?.find((application) => application.Token === route.params.appToken)
    : undefined,
)
</script>

<template>
  <p v-if="error" class="px-2.5 py-1 text-xs text-muted-foreground">
    The applications could not be loaded.
    <button
      type="button"
      class="rounded-sm text-link underline-offset-4 hover:underline focus-visible:outline-2 focus-visible:outline-ring"
      @click="reload"
    >
      Try again
    </button>
  </p>

  <DropdownMenu v-else-if="groups.length > 0">
    <DropdownMenuTrigger
      class="flex w-full items-center gap-2.5 rounded-md py-1.5 pr-2.5 pl-9 text-left text-sm text-muted-foreground transition-colors hover:bg-sidebar-accent hover:text-sidebar-accent-foreground focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring"
    >
      <span class="min-w-0 flex-1 truncate" :class="current && 'text-sidebar-foreground'">
        <span v-if="current" class="sr-only">Current application:</span>
        {{ current?.Name ?? 'Open an application' }}
      </span>
      <ChevronsUpDownIcon class="size-3.5 shrink-0" />
    </DropdownMenuTrigger>

    <DropdownMenuContent align="start" class="min-w-56">
      <template v-for="(group, index) in groups" :key="group.server.ID">
        <DropdownMenuSeparator v-if="index > 0" />
        <DropdownMenuLabel class="truncate">{{ group.server.Name }}</DropdownMenuLabel>
        <DropdownMenuItem v-for="application in group.applications" :key="application.Token" as-child>
          <RouterLink
            :to="{ name: 'app-entities', params: { appToken: application.Token } }"
            :aria-current="application === current ? 'page' : undefined"
          >
            <span class="min-w-0 flex-1 truncate">{{ application.Name }}</span>
            <CheckIcon v-if="application === current" aria-hidden="true" />
          </RouterLink>
        </DropdownMenuItem>
      </template>
    </DropdownMenuContent>
  </DropdownMenu>
</template>
