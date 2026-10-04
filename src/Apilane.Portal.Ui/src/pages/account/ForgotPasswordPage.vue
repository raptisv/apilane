<script setup lang="ts">
import { computed, nextTick, reactive, ref, useTemplateRef } from 'vue'
import AuthForm from '@/components/AuthForm.vue'
import FormField from '@/components/FormField.vue'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { useMutation } from '@/composables/useMutation'
import { api, unwrapAnonymous } from '@/lib/api'
import type { Schemas } from '@/lib/api'
import { formErrors } from '@/lib/forms'

const form = reactive<Schemas['ForgotPasswordRequest']>({ Email: '' })

// The answer is the same whether or not the address belongs to a user, and so is the text below:
// the screen must not reveal which addresses are registered.
// When mail is not set up on this instance the API answers 409; its message shows above the form.
const request = useMutation(() => unwrapAnonymous(api.POST('/api/v1/account/password-reset-requests', { body: form })))

const errors = computed(() => formErrors(request.error.value, ['Email']))
const sent = ref(false)
const heading = useTemplateRef<HTMLElement>('heading')

async function submit(): Promise<boolean> {
  sent.value = await request.run()

  if (sent.value) {
    // The form is gone and focus with it: continue on the confirmation, which is then read out.
    await nextTick()
    heading.value?.focus()
  }

  return sent.value
}
</script>

<template>
  <div v-if="sent" class="text-center">
    <h1 ref="heading" tabindex="-1" class="text-base font-medium outline-none">
      Please check your email to reset your password.
    </h1>
    <Button as-child class="mt-6 w-full">
      <RouterLink :to="{ name: 'login' }">Sign in</RouterLink>
    </Button>
  </div>

  <AuthForm v-else title="Forgot password" submit-label="Reset my password" :submit="submit" :error="errors.message">
    <template #intro>
      <p class="text-sm text-muted-foreground">
        Type the email address that you used to register. We'll send you an email with a link to reset your password.
      </p>
    </template>

    <FormField v-slot="{ field }" label="Email" :error="errors.fields.Email">
      <Input
        v-model="form.Email"
        v-bind="field"
        type="email"
        name="email"
        autocomplete="username"
        spellcheck="false"
      />
    </FormField>

    <template #footer>
      <Button as-child variant="outline" class="w-full">
        <RouterLink :to="{ name: 'login' }">Cancel</RouterLink>
      </Button>
    </template>
  </AuthForm>
</template>
