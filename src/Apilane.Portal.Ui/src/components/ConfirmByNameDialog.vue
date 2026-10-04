<script setup lang="ts">
import { Loader2Icon } from '@lucide/vue'
import { nextTick, ref, useId, useTemplateRef, watch } from 'vue'
import {
  AlertDialog,
  AlertDialogContent,
  AlertDialogDescription,
  AlertDialogFooter,
  AlertDialogHeader,
  AlertDialogTitle,
} from '@/components/ui/alert-dialog'
import { Button } from '@/components/ui/button'
import { Checkbox } from '@/components/ui/checkbox'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import FormField from './FormField.vue'

// Asks before an action that cannot be undone: the button stays disabled until the user has typed
// the exact name of the thing. Use it for every delete. The dialog closes when `action` returns
// true; when it returns false it stays open and shows `error`.
//
//   <ConfirmByNameDialog v-model:open="open" :title="`Delete server ${server.Name}?`" description="..." :name="server.Name"
//     :action="() => remove.run(server.ID)" :error="remove.error.value?.message" />
//
// For the heaviest actions (rebuild or delete an application) add `acknowledge`: a box with that
// text that must be ticked as well. The default slot holds more about the consequences (a list),
// shown under the description.
const props = withDefaults(
  defineProps<{
    title: string
    description: string
    /** What the user has to type, letter for letter. */
    name: string
    confirmLabel?: string
    /** The label of an 'I understand' box that must be ticked too. Without it there is no box. */
    acknowledge?: string
    /** Runs the write. Resolves to true when it succeeded (what useMutation's run returns). */
    action: () => Promise<boolean>
    error?: string
    /** False when the button that opened the dialog is still there after a success (rebuild), so focus goes back to it. */
    openerRemoved?: boolean
  }>(),
  { confirmLabel: 'Delete', openerRemoved: true },
)

const open = defineModel<boolean>('open', { required: true })
const pending = ref(false)
const typed = ref('')
const acknowledged = ref(false)
const input = useTemplateRef('input')
const checkboxId = useId()

// Set when the dialog closes after a success that removes the opener with its row (a delete), so
// focus goes to the screen instead. Not set with `:opener-removed="false"`.
let succeeded = false

// Every opening starts with an empty box and the acknowledgement unticked.
watch(open, () => {
  typed.value = ''
  acknowledged.value = false
})

function ready(): boolean {
  return typed.value === props.name && (!props.acknowledge || acknowledged.value)
}

async function confirm(): Promise<void> {
  if (pending.value || !ready()) {
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

// Focus the first control on open (the acknowledgement, else the name box). Cancel is a plain
// Button on purpose: reka focuses an AlertDialogCancel after this runs. The checkbox is found by id:
// its component renders a fragment, so its $el is an empty text node.
function focusInput(event: Event): void {
  event.preventDefault()
  const first: HTMLElement | null | undefined = props.acknowledge ? document.getElementById(checkboxId) : input.value?.$el
  first?.focus()
}

async function onCloseAutoFocus(event: Event): Promise<void> {
  if (succeeded) {
    succeeded = false
    event.preventDefault()
    document.getElementById('main')?.focus({ preventScroll: true })
    return
  }

  // Cancelled after being opened from a menu item: the item is gone, so focus would drop to <body>.
  await nextTick()

  if (document.activeElement === document.body) {
    document.getElementById('main')?.focus({ preventScroll: true })
  }
}

// Escape and Cancel do nothing while the write is running.
function setOpen(value: boolean): void {
  if (!pending.value) {
    open.value = value
  }
}
</script>

<template>
  <AlertDialog :open="open" @update:open="setOpen">
    <AlertDialogContent @open-auto-focus="focusInput" @close-auto-focus="onCloseAutoFocus">
      <form class="grid gap-4 wrap-anywhere" @submit.prevent="confirm">
        <AlertDialogHeader>
          <AlertDialogTitle>{{ title }}</AlertDialogTitle>
          <AlertDialogDescription>{{ description }}</AlertDialogDescription>
        </AlertDialogHeader>

        <slot />

        <div v-if="acknowledge" class="flex items-start gap-2.5">
          <Checkbox
            :id="checkboxId"
            class="mt-0.5"
            :model-value="acknowledged"
            @update:model-value="(checked) => (acknowledged = checked === true)"
          />
          <Label :for="checkboxId" class="leading-snug font-normal">{{ acknowledge }}</Label>
        </div>

        <FormField v-slot="{ field }" :label="`Type ${name} to confirm`">
          <Input ref="input" v-model="typed" v-bind="field" autocomplete="off" spellcheck="false" />
        </FormField>

        <!-- Not the field's error: what was typed is right, the write failed. -->
        <p v-if="error" role="alert" class="text-sm text-destructive">{{ error }}</p>

        <AlertDialogFooter>
          <Button type="button" variant="outline" :disabled="pending" @click="setOpen(false)">Cancel</Button>
          <Button type="submit" variant="destructive" :disabled="pending || !ready()">
            <Loader2Icon v-if="pending" class="animate-spin" />
            {{ confirmLabel }}
          </Button>
        </AlertDialogFooter>
      </form>
    </AlertDialogContent>
  </AlertDialog>
</template>
