<script setup lang="ts">
import { CircleAlertIcon, Loader2Icon } from '@lucide/vue'
import { nextTick, onMounted, ref, useTemplateRef } from 'vue'
import { Alert, AlertDescription } from '@/components/ui/alert'
import { Button } from '@/components/ui/button'

// A form that is a whole public screen inside AuthLayout (sign in, sign up, password reset): the
// counterpart of FormDialog. Put FormField elements in the default slot and links in `footer`.
// Enter or the button calls `submit`; when it returns false the form shows `error` and the field errors.
//
//   <AuthForm title="Sign in" submit-label="Sign in" :submit="submit" :error="errors.message">
//     <FormField ...>
//     <template #footer>...</template>
//   </AuthForm>
const props = defineProps<{
  title: string
  submitLabel: string
  /** Runs the write. Resolves to true when it succeeded (what useMutation's run returns). */
  submit: () => Promise<boolean>
  /** The form-level message from formErrors() (lib/forms.ts). */
  error?: string
}>()

const pending = ref(false)
const formEl = useTemplateRef<HTMLFormElement>('formEl')

// The form is the whole screen, so typing can start at once. Done here because the `autofocus`
// attribute only works on the first screen after a page load.
onMounted(() => formEl.value?.querySelector('input')?.focus())

async function onSubmit(): Promise<void> {
  if (pending.value) {
    return
  }

  pending.value = true

  try {
    if (!(await props.submit())) {
      // Move to the first field the server rejected, so it is read out and scrolled into view.
      await nextTick()
      formEl.value?.querySelector<HTMLElement>('[aria-invalid="true"]')?.focus()
    }
  } finally {
    pending.value = false
  }
}
</script>

<template>
  <form ref="formEl" class="grid gap-4" novalidate @submit.prevent="onSubmit">
    <h1 class="text-center text-base font-medium">{{ title }}</h1>

    <slot name="intro" />

    <!-- Alert carries role="alert" itself, so the message is read out when it appears. -->
    <Alert v-if="error" variant="destructive">
      <CircleAlertIcon />
      <AlertDescription>{{ error }}</AlertDescription>
    </Alert>

    <slot />

    <Button type="submit" class="w-full" :disabled="pending">
      <Loader2Icon v-if="pending" class="animate-spin" />
      {{ submitLabel }}
    </Button>

    <slot name="footer" />
  </form>
</template>
