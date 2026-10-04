<script setup lang="ts">
import { UsersIcon } from '@lucide/vue'
import { computed, ref } from 'vue'
import ConfirmDialog from '@/components/ConfirmDialog.vue'
import ErrorState from '@/components/ErrorState.vue'
import LoadingState from '@/components/LoadingState.vue'
import PageHeader from '@/components/PageHeader.vue'
import StateMessage from '@/components/StateMessage.vue'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@/components/ui/table'
import { useAsync } from '@/composables/useAsync'
import { useMutation } from '@/composables/useMutation'
import { api, unwrap } from '@/lib/api'
import type { Schemas } from '@/lib/api'
import { formatDateTime, formatUtc } from '@/lib/format'
import * as toast from '@/lib/toast'

type User = Schemas['UserResponse']

// The API returns the users with the latest sign-in first; the table keeps that order.
const { data, error, loading, reload } = useAsync(() => unwrap(api.GET('/api/v1/admin/users')))

// `target` keeps its value after the dialog closes, so the text does not change while it fades out.
const confirmOpen = ref(false)
const target = ref<User>()

const change = useMutation((user: User) =>
  unwrap(
    api.PUT('/api/v1/admin/users/{userId}/role', {
      params: { path: { userId: user.ID } },
      body: { IsAdmin: !user.IsAdmin },
    }),
  ),
)

const confirmText = computed(() => {
  const user = target.value

  if (!user) {
    return { title: '', description: '', label: '' }
  }

  return user.IsAdmin
    ? {
        title: 'Make ordinary user?',
        description: `${user.Email} will no longer be an administrator of this instance. The change takes effect within 30 minutes, or at once when they sign in again.`,
        label: 'Make user',
      }
    : {
        title: 'Make administrator?',
        description: `${user.Email} will be able to manage the servers, users and settings of this instance. The change takes effect within 30 minutes, or at once when they sign in again.`,
        label: 'Make admin',
      }
})

function openConfirm(user: User): void {
  target.value = user
  change.reset()
  confirmOpen.value = true
}

async function submit(): Promise<boolean> {
  const user = target.value

  if (!user || !(await change.run(user))) {
    return false
  }

  toast.success(user.IsAdmin ? `${user.Email} is now an ordinary user.` : `${user.Email} is now an administrator.`)
  void reload()
  return true
}
</script>

<template>
  <PageHeader
    title="Users"
    description="Everyone who can sign in to this instance. A role change takes effect within 30 minutes, or at once when the user signs in again."
  />

  <LoadingState v-if="loading" label="Loading users" />

  <ErrorState v-else-if="error" :error="error" @retry="reload" />

  <StateMessage v-else-if="!data || data.Data.length === 0" :icon="UsersIcon" title="No users found" />

  <div v-else class="overflow-hidden rounded-lg border bg-card">
    <Table>
      <TableHeader>
        <TableRow>
          <TableHead class="pl-4">User</TableHead>
          <TableHead>Role</TableHead>
          <TableHead class="hidden md:table-cell">Last sign-in</TableHead>
          <TableHead class="pr-4"><span class="sr-only">Actions</span></TableHead>
        </TableRow>
      </TableHeader>
      <TableBody>
        <TableRow v-for="user in data.Data" :key="user.ID">
          <TableCell class="pl-4 whitespace-normal">
            <p class="font-medium wrap-anywhere">
              {{ user.Email }}
              <Badge v-if="user.IsCurrentUser" variant="outline" class="ml-1 align-middle">You</Badge>
            </p>
            <p class="mt-0.5 font-mono text-xs break-all text-muted-foreground">{{ user.ID }}</p>
            <!-- On a phone the last sign-in moves under the name, so the table fits the screen. -->
            <p class="mt-0.5 text-xs text-muted-foreground md:hidden">
              Last sign-in
              <time :datetime="user.LastLogin" :title="`${formatUtc(user.LastLogin)} UTC`">
                {{ formatDateTime(user.LastLogin) }}
              </time>
            </p>
          </TableCell>
          <TableCell>
            <Badge :variant="user.IsAdmin ? 'default' : 'secondary'">{{ user.IsAdmin ? 'Admin' : 'User' }}</Badge>
          </TableCell>
          <TableCell class="hidden text-muted-foreground md:table-cell">
            <time :datetime="user.LastLogin" :title="`${formatUtc(user.LastLogin)} UTC`">
              {{ formatDateTime(user.LastLogin) }}
            </time>
          </TableCell>
          <TableCell class="w-px pr-4">
            <!-- Nobody can change their own role, so one administrator always remains. -->
            <Button v-if="!user.IsCurrentUser" variant="outline" size="sm" @click="openConfirm(user)">
              {{ user.IsAdmin ? 'Make user' : 'Make admin' }}
              <span class="sr-only">: {{ user.Email }}</span>
            </Button>
          </TableCell>
        </TableRow>
      </TableBody>
    </Table>
  </div>

  <ConfirmDialog
    v-model:open="confirmOpen"
    :title="confirmText.title"
    :description="confirmText.description"
    :confirm-label="confirmText.label"
    :action="submit"
    :error="change.error.value?.message"
  />
</template>
