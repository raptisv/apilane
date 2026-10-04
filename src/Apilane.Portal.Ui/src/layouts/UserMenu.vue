<script setup lang="ts">
import { BookOpenIcon, ChevronsUpDownIcon, KeyRoundIcon, LogOutIcon } from '@lucide/vue'
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuLabel,
  DropdownMenuSeparator,
  DropdownMenuTrigger,
} from '@/components/ui/dropdown-menu'
import { useMutation } from '@/composables/useMutation'
import { api, loginUrl, unwrap } from '@/lib/api'
import { useSession } from '@/lib/session'
import * as toast from '@/lib/toast'

// `changePassword` asks AppShell to open the change-password dialog over the current screen.
const emit = defineEmits<{ changePassword: [] }>()

const session = useSession()

// The Portal's own API reference (Swagger UI of /api/v1). The Portal serves it itself, not this app, so it is a plain link.
const apiReferenceUrl = '/swagger'

const signOut = useMutation(() => unwrap(api.DELETE('/api/v1/session')))

async function onSignOut(): Promise<void> {
  if (await signOut.run()) {
    // A full page load, so nothing of this session stays in memory.
    location.assign(loginUrl())
  } else {
    toast.error(signOut.error.value)
  }
}
</script>

<template>
  <DropdownMenu>
    <DropdownMenuTrigger
      class="flex w-full items-center gap-2.5 rounded-md px-2.5 py-2 text-left text-sm text-sidebar-foreground transition-colors hover:bg-sidebar-accent focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring"
    >
      <span
        class="flex size-7 shrink-0 items-center justify-center rounded-full bg-primary/15 text-xs font-semibold uppercase text-link"
        aria-hidden="true"
      >
        {{ session.Email.charAt(0) }}
      </span>
      <span class="min-w-0 flex-1">
        <span class="block truncate">{{ session.Email }}</span>
        <span v-if="session.IsAdmin" class="block text-xs text-muted-foreground">Administrator</span>
      </span>
      <ChevronsUpDownIcon class="size-4 shrink-0 text-muted-foreground" />
    </DropdownMenuTrigger>

    <DropdownMenuContent align="start" side="top" class="w-56">
      <DropdownMenuLabel class="truncate">{{ session.Email }}</DropdownMenuLabel>
      <DropdownMenuSeparator />
      <DropdownMenuItem @select="emit('changePassword')">
        <KeyRoundIcon />
        Change password
      </DropdownMenuItem>
      <DropdownMenuItem as-child>
        <a :href="apiReferenceUrl" target="_blank" rel="noopener">
          <BookOpenIcon />
          API reference
        </a>
      </DropdownMenuItem>
      <DropdownMenuSeparator />
      <DropdownMenuItem :disabled="signOut.pending.value" @select="onSignOut">
        <LogOutIcon />
        Sign out
      </DropdownMenuItem>
    </DropdownMenuContent>
  </DropdownMenu>
</template>
