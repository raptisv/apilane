<script setup lang="ts">
import { CircleAlertIcon, Loader2Icon } from '@lucide/vue'
import { nextTick, ref, useTemplateRef } from 'vue'
import { Alert, AlertDescription } from '@/components/ui/alert'
import { Button } from '@/components/ui/button'
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from '@/components/ui/dialog'

// A dialog holding a form: use it for every create and edit. Put FormField elements in the
// default slot. Enter or the submit button calls `submit`; the dialog closes when it returns true
// and stays open, showing `error` and the field errors, when it returns false.
//
//   <FormDialog v-model:open="open" title="Add server" :submit="() => save.run(form)" :error="errors.message">
//     <FormField ...>
//   </FormDialog>
//
// `wide` is for a form with a large field and something next to it (an e-mail body and its preview).
const props = withDefaults(
  defineProps<{
    title: string
    description?: string
    submitLabel?: string
    /** Runs the write. Resolves to true when it succeeded (what useMutation's run returns). */
    submit: () => Promise<boolean>
    /** The form-level message from formErrors() (lib/forms.ts). */
    error?: string
    wide?: boolean
  }>(),
  { submitLabel: 'Save' },
)

const open = defineModel<boolean>('open', { required: true })
const pending = ref(false)
const formEl = useTemplateRef<HTMLFormElement>('formEl')

async function onSubmit(): Promise<void> {
  if (pending.value) {
    return
  }

  pending.value = true

  try {
    if (await props.submit()) {
      open.value = false
    } else {
      // Move to the first field the server rejected, so it is read out and scrolled into view.
      await nextTick()
      formEl.value?.querySelector<HTMLElement>('[aria-invalid="true"]')?.focus()
    }
  } finally {
    pending.value = false
  }
}

// Opened from a menu item, the dialog has nothing to hand focus back to (the item is gone), so
// focus would drop to <body>: it goes to the screen instead.
async function onCloseAutoFocus(): Promise<void> {
  await nextTick()

  if (document.activeElement === document.body) {
    document.getElementById('main')?.focus({ preventScroll: true })
  }
}

// Escape and the close button do nothing while the write is running.
function setOpen(value: boolean): void {
  if (!pending.value) {
    open.value = value
  }
}
</script>

<template>
  <Dialog :open="open" @update:open="setOpen">
    <!--
      A click outside does not close the dialog: it would throw away what was typed.
      Without a description, aria-describedby is cleared so it does not point at nothing.
    -->
    <DialogContent
      class="max-h-[calc(100dvh-2rem)] overflow-y-auto"
      :class="wide ? 'sm:max-w-4xl' : 'sm:max-w-lg'"
      v-bind="description ? {} : { 'aria-describedby': undefined }"
      @interact-outside="(event: Event) => event.preventDefault()"
      @close-auto-focus="onCloseAutoFocus"
    >
      <form ref="formEl" class="grid gap-4" novalidate @submit.prevent="onSubmit">
        <DialogHeader class="pr-8">
          <DialogTitle>{{ title }}</DialogTitle>
          <DialogDescription v-if="description">{{ description }}</DialogDescription>
        </DialogHeader>

        <Alert v-if="error" variant="destructive" role="alert">
          <CircleAlertIcon />
          <AlertDescription>{{ error }}</AlertDescription>
        </Alert>

        <slot />

        <DialogFooter>
          <Button type="button" variant="outline" :disabled="pending" @click="setOpen(false)">Cancel</Button>
          <Button type="submit" :disabled="pending">
            <Loader2Icon v-if="pending" class="animate-spin" />
            {{ submitLabel }}
          </Button>
        </DialogFooter>
      </form>
    </DialogContent>
  </Dialog>
</template>
