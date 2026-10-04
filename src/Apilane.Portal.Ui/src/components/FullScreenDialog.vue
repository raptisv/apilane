<script setup lang="ts">
import { nextTick } from 'vue'
import { Dialog, DialogContent, DialogDescription, DialogHeader, DialogTitle } from '@/components/ui/dialog'

// A read-only view that needs the whole window (the security tree and matrix): a dialog that fills
// the screen, with a title, a line under it and a body that scrolls. For a form use FormDialog.
//
//   <FullScreenDialog v-model:open="open" title="Security matrix" description="...">...</FullScreenDialog>
defineProps<{
  title: string
  description?: string
}>()

const open = defineModel<boolean>('open', { required: true })

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
    <DialogContent
      class="top-0 left-0 flex h-dvh max-h-dvh w-screen max-w-none translate-x-0 translate-y-0 flex-col gap-0 rounded-none p-0 sm:top-4 sm:left-4 sm:h-[calc(100dvh-2rem)] sm:w-[calc(100vw-2rem)] sm:max-w-none sm:rounded-xl"
      @close-auto-focus="onCloseAutoFocus"
    >
      <DialogHeader class="border-b px-4 py-3 pr-12">
        <DialogTitle>{{ title }}</DialogTitle>
        <DialogDescription v-if="description">{{ description }}</DialogDescription>
        <slot name="header" />
      </DialogHeader>
      <div class="min-h-0 flex-1 overflow-auto p-4">
        <slot />
      </div>
    </DialogContent>
  </Dialog>
</template>
