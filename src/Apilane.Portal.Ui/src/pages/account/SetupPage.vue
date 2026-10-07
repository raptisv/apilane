<script setup lang="ts">
import { computed, reactive } from 'vue'
import { useRouter } from 'vue-router'
import AuthForm from '@/components/AuthForm.vue'
import FormField from '@/components/FormField.vue'
import { Input } from '@/components/ui/input'
import { useMutation } from '@/composables/useMutation'
import { api, unwrapAnonymous } from '@/lib/api'
import type { Schemas } from '@/lib/api'
import { completeBootstrap } from '@/lib/bootstrap'
import { formErrors } from '@/lib/forms'
import { setSession } from '@/lib/session'

const router = useRouter()
const form = reactive<Schemas['BootstrapRequest']>({ Email: '', TemporaryPassword: '', Password: '', ConfirmPassword: '' })
const setup = useMutation(async () => {
  setSession(await unwrapAnonymous(api.POST('/api/v1/bootstrap', { body: form })))
  completeBootstrap()
  form.TemporaryPassword = ''
  form.Password = ''
  form.ConfirmPassword = ''
})
const errors = computed(() => formErrors(setup.error.value, ['Email', 'TemporaryPassword', 'Password', 'ConfirmPassword']))

async function submit(): Promise<boolean> {
  if (!(await setup.run())) {
    return false
  }

  await router.replace({ name: 'apps' })
  return true
}
</script>

<template>
  <AuthForm title="Set up your administrator account" submit-label="Create account and continue" :submit="submit" :error="errors.message">
    <p class="text-sm text-muted-foreground">
      Choose the email address and password for your administrator account. Enter the temporary password from
      your server's startup output. Ask the server operator for it if you do not have access to that output.
    </p>

    <FormField v-slot="{ field }" label="Administrator email" help="Use the email address you want to sign in with." :error="errors.fields.Email">
      <Input v-model="form.Email" v-bind="field" type="email" name="email" autocomplete="username" spellcheck="false" />
    </FormField>
    <FormField v-slot="{ field }" label="Temporary password" :error="errors.fields.TemporaryPassword">
      <Input v-model="form.TemporaryPassword" v-bind="field" type="password" name="temporary-password" autocomplete="current-password" />
    </FormField>
    <FormField v-slot="{ field }" label="New password" help="8 to 100 characters. Choose a different password from the temporary one." :error="errors.fields.Password">
      <Input v-model="form.Password" v-bind="field" type="password" name="new-password" autocomplete="new-password" />
    </FormField>
    <FormField v-slot="{ field }" label="Confirm new password" :error="errors.fields.ConfirmPassword">
      <Input v-model="form.ConfirmPassword" v-bind="field" type="password" name="confirm-password" autocomplete="new-password" />
    </FormField>
  </AuthForm>
</template>
