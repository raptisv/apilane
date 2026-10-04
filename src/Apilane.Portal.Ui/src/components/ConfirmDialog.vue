<script setup lang="ts">
import { Loader2Icon } from '@lucide/vue'
import { nextTick, ref } from 'vue'
import {
  AlertDialog,
  AlertDialogCancel,
  AlertDialogContent,
  AlertDialogDescription,
  AlertDialogFooter,
  AlertDialogHeader,
  AlertDialogTitle,
} from '@/components/ui/alert-dialog'
import { Button } from '@/components/ui/button'

// Asks before an action that can be undone or repeated (change a role, reset a cache). For a
// delete use ConfirmByNameDialog. The dialog closes when `action` returns true; when it returns
// false it stays open and shows `error`.
//
//   <ConfirmDialog v-model:open="open" title="Make admin?" description="..." :action="() => change.run(user)"
//     :error="change.error.value?.message" />
const props = withDefaults(
  defineProps<{
    title: string
    description: string
    confirmLabel?: string
    /** A red confirm button, for the harmful direction (take an application offline). */
    destructive?: boolean
    /** Runs the write. Resolves to true when it succeeded (what useMutation's run returns). */
    action: () => Promise<boolean>
    error?: string
    /** True when a success removes the button that opened the dialog (the row of a deleted record): focus then goes to the screen. */
    openerRemoved?: boolean
  }>(),
  { confirmLabel: 'Confirm' },
)

const open = defineModel<boolean>('open', { required: true })
const pending = ref(false)

// Set when the dialog closes after a success that removes its opener (`opener-removed`).
let succeeded = false

async function confirm(): Promise<void> {
  if (pending.value) {
    return
  }

  pending.value = true

  try {
    if (await props.action()) {
      succeeded = props.openerRemoved
      open.value = false
    }
  } finally {
    pending.value = false
  }
}

// Escape and Cancel do nothing while the write is running.
function setOpen(value: boolean): void {
  if (!pending.value) {
    open.value = value
  }
}

async function onCloseAutoFocus(event: Event): Promise<void> {
  // The opener is still there now and goes when the list reloads: focus must not return to it.
  if (succeeded) {
    succeeded = false
    event.preventDefault()
    document.getElementById('main')?.focus({ preventScroll: true })
    return
  }

  // Opened from a menu item, the dialog has nothing to hand focus back to (the item is gone), so
  // focus would drop to <body>: it goes to the screen instead.
  await nextTick()

  if (document.activeElement === document.body) {
    document.getElementById('main')?.focus({ preventScroll: true })
  }
}
</script>

<template>
  <AlertDialog :open="open" @update:open="setOpen">
    <AlertDialogContent class="wrap-anywhere" @close-auto-focus="onCloseAutoFocus">
      <AlertDialogHeader>
        <AlertDialogTitle>{{ title }}</AlertDialogTitle>
        <AlertDialogDescription>{{ description }}</AlertDialogDescription>
      </AlertDialogHeader>

      <p v-if="error" role="alert" class="text-sm text-destructive">{{ error }}</p>

      <AlertDialogFooter>
        <AlertDialogCancel :disabled="pending">Cancel</AlertDialogCancel>
        <Button :variant="destructive ? 'destructive' : 'default'" :disabled="pending" @click="confirm">
          <Loader2Icon v-if="pending" class="animate-spin" />
          {{ confirmLabel }}
        </Button>
      </AlertDialogFooter>
    </AlertDialogContent>
  </AlertDialog>
</template>
