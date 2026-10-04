<script setup lang="ts">
import { MenuIcon } from '@lucide/vue'
import { useMediaQuery } from '@vueuse/core'
import { computed, nextTick, ref, useTemplateRef, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import ChangePasswordDialog from '@/components/ChangePasswordDialog.vue'
import ForbiddenState from '@/components/ForbiddenState.vue'
import { Button } from '@/components/ui/button'
import { Sheet, SheetContent, SheetDescription, SheetHeader, SheetTitle, SheetTrigger } from '@/components/ui/sheet'
import { useSession } from '@/lib/session'
import SidebarNav from './SidebarNav.vue'
import UserMenu from './UserMenu.vue'

const session = useSession()
const route = useRoute()
const router = useRouter()

const logo = '/favicon.ico'

// The instance name with its logo: a link to the applications page.
const brandClass = 'flex min-w-0 items-center gap-2.5 focus-visible:outline-2 focus-visible:-outline-offset-2 focus-visible:outline-ring'
const footerLinkClass = 'rounded-sm underline-offset-4 hover:underline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring'

// The one place that enforces meta.requiresAdmin: the screen is replaced, the address stays.
// The API refuses the same calls on its own; this only spares the user a failed request.
const forbidden = computed(() => route.meta.requiresAdmin === true && !session.value.IsAdmin)

// Phone drawer. From `lg` up the sidebar is always visible and this stays closed.
const drawerOpen = ref(false)
const isDesktop = useMediaQuery('(min-width: 1024px)')
const main = useTemplateRef('main')

// Set when the drawer closes because a link was followed: focus then belongs in the new screen,
// not back on the menu button.
let closedByNavigation = false

function focusMain(): void {
  main.value?.focus({ preventScroll: true })
}

function closeDrawerAfterNavigation(): void {
  if (drawerOpen.value) {
    closedByNavigation = true
    drawerOpen.value = false
  }
}

function onDrawerCloseAutoFocus(event: Event): void {
  if (closedByNavigation) {
    closedByNavigation = false
    event.preventDefault()
    focusMain()
  }
}

// Back, Forward and links without the `navigate` emit also close the drawer.
watch(() => route.fullPath, closeDrawerAfterNavigation)

// A new screen: keyboard and screen-reader users continue in its content, not in the sidebar.
// Watches the path only, so a query change (a page number, a dialog) leaves focus where it is.
watch(
  () => route.path,
  async () => {
    await nextTick()
    focusMain()
  },
)

// The change-password dialog. The user menu opens it over the current screen; the address
// /account/password opens it too, and closing it there leaves that address for the applications page.
const passwordOpen = ref(false)

function openPasswordDialog(): void {
  drawerOpen.value = false
  passwordOpen.value = true
}

watch(
  () => route.name,
  (name) => {
    if (name === 'change-password') {
      passwordOpen.value = true
    }
  },
  { immediate: true },
)

watch(passwordOpen, async (open) => {
  if (open) {
    return
  }

  if (route.name === 'change-password') {
    void router.replace({ name: 'apps' })
  } else {
    // Opened from the user menu: the menu item that had focus is gone by now, so the dialog has
    // nothing to hand focus back to and it would drop to <body>.
    await nextTick()
    focusMain()
  }
})

// Rotating a tablet to landscape shows the sidebar; the drawer must not stay open on top of it.
watch(isDesktop, (desktop) => {
  if (desktop) {
    drawerOpen.value = false
  }
})
</script>

<template>
  <div class="min-h-dvh">
    <a
      href="#main"
      class="sr-only focus:not-sr-only focus:fixed focus:left-3 focus:top-3 focus:z-50 focus:rounded-md focus:bg-primary focus:px-3 focus:py-2 focus:text-sm focus:text-primary-foreground"
      @click.prevent="focusMain"
    >
      Skip to content
    </a>

    <!-- Desktop sidebar -->
    <aside class="fixed inset-y-0 left-0 z-30 hidden w-60 flex-col border-r border-sidebar-border bg-sidebar lg:flex">
      <RouterLink :to="{ name: 'apps' }" :class="brandClass" class="h-14 shrink-0 px-4">
        <img :src="logo" alt="" class="size-6 rounded" />
        <span class="truncate text-sm font-semibold">{{ session.InstanceTitle }}</span>
      </RouterLink>
      <div class="flex-1 overflow-y-auto px-2 pb-2">
        <SidebarNav />
      </div>
      <div class="border-t border-sidebar-border p-2">
        <UserMenu @change-password="openPasswordDialog" />
        <p class="flex items-center justify-between gap-2 px-2.5 pt-1 text-xs text-muted-foreground">
          <a href="https://github.com/raptisv/apilane" target="_blank" rel="noopener" :class="footerLinkClass">
            v{{ session.Version }}
            <span class="sr-only">on GitHub (opens in a new tab)</span>
          </a>
          <a href="https://apilane.com" target="_blank" rel="noopener" :class="footerLinkClass">
            apilane.com
            <span class="sr-only">(opens in a new tab)</span>
          </a>
        </p>
      </div>
    </aside>

    <!-- Phone top bar with the same navigation in a drawer -->
    <header
      class="sticky top-0 z-30 flex h-14 items-center gap-2 border-b border-sidebar-border bg-sidebar/95 px-3 backdrop-blur lg:hidden"
    >
      <Sheet v-model:open="drawerOpen">
        <SheetTrigger as-child>
          <Button variant="ghost" size="icon" aria-label="Open navigation">
            <MenuIcon />
          </Button>
        </SheetTrigger>
        <SheetContent side="left" class="w-72 gap-0 bg-sidebar p-0" @close-auto-focus="onDrawerCloseAutoFocus">
          <SheetHeader class="h-14 flex-row items-center gap-2.5 px-4">
            <img :src="logo" alt="" class="size-6 rounded" />
            <SheetTitle class="truncate text-sm">{{ session.InstanceTitle }}</SheetTitle>
            <SheetDescription class="sr-only">Navigation</SheetDescription>
          </SheetHeader>
          <div class="flex-1 overflow-y-auto px-2 pb-2">
            <SidebarNav @navigate="closeDrawerAfterNavigation" />
          </div>
          <div class="border-t border-sidebar-border p-2">
            <UserMenu @change-password="openPasswordDialog" />
            <p class="flex items-center justify-between gap-2 px-2.5 pt-1 text-xs text-muted-foreground">
              <a href="https://github.com/raptisv/apilane" target="_blank" rel="noopener" :class="footerLinkClass">
                v{{ session.Version }}
                <span class="sr-only">on GitHub (opens in a new tab)</span>
              </a>
              <a href="https://apilane.com" target="_blank" rel="noopener" :class="footerLinkClass">
                apilane.com
                <span class="sr-only">(opens in a new tab)</span>
              </a>
            </p>
          </div>
        </SheetContent>
      </Sheet>
      <RouterLink :to="{ name: 'apps' }" :class="brandClass" class="rounded-md">
        <img :src="logo" alt="" class="size-6 rounded" />
        <span class="truncate text-sm font-semibold">{{ session.InstanceTitle }}</span>
      </RouterLink>
    </header>

    <main id="main" ref="main" tabindex="-1" class="outline-none lg:pl-60">
      <div class="mx-auto w-full max-w-6xl px-4 py-6 sm:px-6 lg:px-8 lg:py-8">
        <ForbiddenState v-if="forbidden" />
        <RouterView v-else />
      </div>
    </main>

    <ChangePasswordDialog v-model:open="passwordOpen" />
  </div>
</template>
