<script setup lang="ts">
import { nextTick, shallowRef, watch } from 'vue'
import CopyField from '@/components/CopyField.vue'
import { Button } from '@/components/ui/button'
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from '@/components/ui/dialog'
import { Skeleton } from '@/components/ui/skeleton'
import { api, unwrap } from '@/lib/api'

// What a developer needs to connect a client to an application: the API server address, the
// application token and the encryption key. Address and token are passed in; the key is fetched
// each time the dialog opens and forgotten when it closes. If the key cannot be loaded the
// dialog still shows the other two.
//
//   <ApplicationInfoDialog v-model:open="open" :name="app.Name" :token="app.Token" :server-url="app.Server.ServerUrl" />
const props = defineProps<{
  name: string
  token: string
  serverUrl: string
}>()

const open = defineModel<boolean>('open', { required: true })

const encryptionKey = shallowRef<string>()
const keyError = shallowRef<Error>()

// Only the latest load may write its result: the dialog can close, or open for another
// application, while a call is still running.
let latest = 0

async function loadKey(): Promise<void> {
  const id = ++latest

  encryptionKey.value = undefined
  keyError.value = undefined

  try {
    const info = await unwrap(
      api.GET('/api/v1/applications/{appToken}/connection-info', { params: { path: { appToken: props.token } } }),
    )

    if (id === latest) {
      encryptionKey.value = info.EncryptionKey
    }
  } catch (e) {
    if (id === latest) {
      keyError.value = e instanceof Error ? e : new Error(String(e))
    }
  }
}

watch(
  [open, () => props.token],
  ([isOpen]) => {
    if (isOpen) {
      void loadKey()
    } else {
      // The key is a secret: it does not stay in memory after the dialog closes.
      latest++
      encryptionKey.value = undefined
      keyError.value = undefined
    }
  },
  { immediate: true },
)

// Opened from a menu item, the dialog has nothing to hand focus back to (the item is gone), so
// focus would drop to <body>: it goes to the screen instead.
async function onCloseAutoFocus(): Promise<void> {
  await nextTick()

  if (document.activeElement === document.body) {
    document.getElementById('main')?.focus({ preventScroll: true })
  }
}
</script>

<template>
  <Dialog v-model:open="open">
    <DialogContent class="max-h-[calc(100dvh-2rem)] overflow-y-auto sm:max-w-lg" @close-auto-focus="onCloseAutoFocus">
      <DialogHeader class="pr-8">
        <DialogTitle>Application info</DialogTitle>
        <DialogDescription>What a client needs to connect to {{ name }}.</DialogDescription>
      </DialogHeader>

      <CopyField label="API server URL" :value="serverUrl" />
      <CopyField label="Token" :value="token" />

      <CopyField v-if="encryptionKey !== undefined" label="Encryption key" :value="encryptionKey" secret />

      <div v-else class="grid gap-1.5">
        <p class="text-sm font-medium">Encryption key</p>
        <div v-if="keyError" role="alert" class="flex flex-wrap items-center gap-x-3 gap-y-1 text-sm text-destructive">
          <span class="min-w-0 wrap-anywhere">Could not load the encryption key: {{ keyError.message }}</span>
          <Button type="button" variant="outline" size="sm" @click="loadKey">Try again</Button>
        </div>
        <div v-else role="status">
          <span class="sr-only">Loading the encryption key</span>
          <Skeleton class="h-8 w-full" />
        </div>
      </div>

      <DialogFooter>
        <Button type="button" @click="open = false">Close</Button>
      </DialogFooter>
    </DialogContent>
  </Dialog>
</template>
