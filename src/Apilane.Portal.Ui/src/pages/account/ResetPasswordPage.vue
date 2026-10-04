<script setup lang="ts">
import { computed, nextTick, reactive, ref, useTemplateRef } from 'vue'
import { useRoute } from 'vue-router'
import AuthForm from '@/components/AuthForm.vue'
import FormField from '@/components/FormField.vue'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { useMutation } from '@/composables/useMutation'
import { api, unwrapAnonymous } from '@/lib/api'
import { formErrors } from '@/lib/forms'

const route = useRoute()

// The reset mail links here with ?code=. Without a code there is nothing to reset with.
const code = computed(() => (typeof route.query.code === 'string' ? route.query.code : ''))

const form = reactive({
  Email: '',
  Password: '',
  ConfirmPassword: '',
})

const reset = useMutation(() =>
  unwrapAnonymous(api.POST('/api/v1/account/password-resets', { body: { ...form, Code: code.value } })),
)

// A used, expired or foreign code is reported on 'Code'. It has no input, so it shows above the form.
const errors = computed(() => formErrors(reset.error.value, ['Email', 'Password', 'ConfirmPassword']))
const done = ref(false)
const heading = useTemplateRef<HTMLElement>('heading')

async function submit(): Promise<boolean> {
  done.value = await reset.run()

  if (done.value) {
    // The form is gone and focus with it: continue on the confirmation, which is then read out.
    await nextTick()
    heading.value?.focus()
  }

  return done.value
}
</script>

<template>
  <div v-if="!code" class="text-center">
    <h1 class="text-base font-medium">This link is not valid</h1>
    <p class="mt-2 text-sm text-muted-foreground">
      The link is incomplete. Open the link from the reset email again, or ask for a new one.
    </p>
    <Button as-child class="mt-6 w-full">
      <RouterLink :to="{ name: 'forgot-password' }">Ask for a new link</RouterLink>
    </Button>
  </div>

  <div v-else-if="done" class="text-center">
    <h1 ref="heading" tabindex="-1" class="text-base font-medium outline-none">Your password has been reset.</h1>
    <Button as-child class="mt-6 w-full">
      <RouterLink :to="{ name: 'login' }">Sign in</RouterLink>
    </Button>
  </div>

  <AuthForm v-else title="Reset password" submit-label="Reset" :submit="submit" :error="errors.message">
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

    <FormField v-slot="{ field }" label="New password" help="At least 8 characters." :error="errors.fields.Password">
      <Input v-model="form.Password" v-bind="field" type="password" name="new-password" autocomplete="new-password" />
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

    <template #footer>
      <p class="text-center text-sm">
        <RouterLink
          :to="{ name: 'forgot-password' }"
          class="rounded-sm text-link underline-offset-4 hover:underline focus-visible:outline-2 focus-visible:outline-ring"
        >
          Ask for a new link
        </RouterLink>
      </p>
    </template>
  </AuthForm>
</template>
