<script setup lang="ts">
import { AppWindowIcon, HistoryIcon, LayoutGridIcon, ServerIcon, SettingsIcon, UsersIcon } from '@lucide/vue'
import { computed } from 'vue'
import type { Component } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { useSession } from '@/lib/session'
import AppSwitcher from './AppSwitcher.vue'

// The same navigation is shown in the desktop sidebar and in the phone drawer.
// `navigate` lets the drawer close itself when a link is followed.
const emit = defineEmits<{ navigate: [] }>()

const session = useSession()
const route = useRoute()
const router = useRouter()

// Vue Router marks a link active only on its own route and that route's children. 'apps/new' and
// 'apps/import' are siblings of 'apps' (see router.ts), so the path decides for this one link.
const inApplications = computed(() => route.path === '/apps' || route.path.startsWith('/apps/'))

// The 'Instance' group, shown to administrators. A new admin screen adds one line here with its
// `route`, which carries meta.requiresAdmin (see router.ts).
const instanceLinks: { route: string; label: string; icon: Component }[] = [
  { route: 'admin-servers', label: 'Servers', icon: ServerIcon },
  { route: 'admin-users', label: 'Users', icon: UsersIcon },
  { route: 'admin-applications', label: 'Applications', icon: LayoutGridIcon },
  { route: 'admin-settings', label: 'Settings', icon: SettingsIcon },
  { route: 'admin-audit-log', label: 'Audit log', icon: HistoryIcon },
]

// The path decides here too: a screen below a link's address is a sibling route, not a child
// (/admin/applications/<token>/data under /admin/applications), and keeps the link lit.
function inSection(name: string): boolean {
  const path = router.resolve({ name }).path

  return route.path === path || route.path.startsWith(`${path}/`)
}

// The base text colour sits on <nav> and is inherited, so it cannot compete with the active
// colour: two colour utilities on the same element do not override each other reliably.
const linkClass =
  'flex items-center gap-2.5 rounded-md px-2.5 py-1.5 text-sm transition-colors hover:bg-sidebar-accent hover:text-sidebar-accent-foreground focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring'
const activeClass = 'bg-sidebar-accent text-sidebar-accent-foreground font-medium'
const groupClass = 'px-2.5 pb-1 pt-4 text-xs font-medium uppercase tracking-wider text-muted-foreground'
</script>

<template>
  <nav class="flex flex-col gap-0.5 text-sidebar-foreground" aria-label="Main">
    <RouterLink :to="{ name: 'apps' }" :class="[linkClass, inApplications && activeClass]" @click="emit('navigate')">
      <AppWindowIcon class="size-4" />
      Applications
    </RouterLink>
    <AppSwitcher />

    <template v-if="session.IsAdmin">
      <p :class="groupClass">Instance</p>
      <RouterLink
        v-for="link in instanceLinks"
        :key="link.route"
        :to="{ name: link.route }"
        :class="[linkClass, inSection(link.route) && activeClass]"
        @click="emit('navigate')"
      >
        <component :is="link.icon" class="size-4" />
        {{ link.label }}
      </RouterLink>
    </template>
  </nav>
</template>
