<script setup lang="ts">
import { computed, reactive, watch } from 'vue'
import FormDialog from '@/components/FormDialog.vue'
import FormField from '@/components/FormField.vue'
import { Input } from '@/components/ui/input'
import { useMutation } from '@/composables/useMutation'
import { api, unwrap } from '@/lib/api'
import type { Schemas } from '@/lib/api'
import { formErrors } from '@/lib/forms'
import { loadSession, useSession } from '@/lib/session'
import * as toast from '@/lib/toast'

// The signed-in user changes their own password. AppShell owns the one instance: it opens from
// the user menu and from the address /account/password.
const open = defineModel<boolean>('open', { required: true })

const session = useSession()

const form = reactive<Schemas['ChangePasswordRequest']>({ OldPassword: '', NewPassword: '', ConfirmPassword: '' })

const save = useMutation(() => unwrap(api.PUT('/api/v1/account/password', { body: form })))
const errors = computed(() => formErrors(save.error.value, ['OldPassword', 'NewPassword', 'ConfirmPassword']))

// Every opening starts empty, also after a cancelled attempt.
watch(open, (isOpen) => {
  if (isOpen) {
    form.OldPassword = ''
    form.NewPassword = ''
    form.ConfirmPassword = ''
    save.reset()
  }
})

async function submit(): Promise<boolean> {
  if (!(await save.run())) {
    return false
  }

  toast.success('Password changed.')

  // The Portal answered with a new session cookie and the user stays signed in. Load the session
  // again so the app holds what belongs to that cookie.
  loadSession().catch(() => {
    // The password is changed; the session on screen simply stays as it was.
  })

  return true
}
</script>

<template>
  <FormDialog
    v-model:open="open"
    title="Change password"
    description="You stay signed in here. Other browsers signed in to this account are signed out."
    submit-label="Change my password"
    :submit="submit"
    :error="errors.message"
  >
    <!-- Not shown: it tells a password manager which account the new password belongs to. -->
    <input type="text" name="email" autocomplete="username" :value="session.Email" readonly hidden />

    <FormField v-slot="{ field }" label="Current password" :error="errors.fields.OldPassword">
      <Input
        v-model="form.OldPassword"
        v-bind="field"
        type="password"
        name="current-password"
        autocomplete="current-password"
      />
    </FormField>

    <FormField v-slot="{ field }" label="New password" help="At least 8 characters." :error="errors.fields.NewPassword">
      <Input v-model="form.NewPassword" v-bind="field" type="password" name="new-password" autocomplete="new-password" />
    </FormField>

    <FormField v-slot="{ field }" label="Confirm new password" :error="errors.fields.ConfirmPassword">
      <Input
        v-model="form.ConfirmPassword"
        v-bind="field"
        type="password"
        name="confirm-password"
        autocomplete="new-password"
      />
    </FormField>
  </FormDialog>
</template>
