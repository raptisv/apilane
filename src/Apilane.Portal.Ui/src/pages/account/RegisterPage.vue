<script setup lang="ts">
import { computed, reactive } from 'vue'
import { useRouter } from 'vue-router'
import AuthForm from '@/components/AuthForm.vue'
import ErrorState from '@/components/ErrorState.vue'
import FormField from '@/components/FormField.vue'
import LoadingState from '@/components/LoadingState.vue'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { useInstance } from '@/composables/useInstance'
import { useMutation } from '@/composables/useMutation'
import { api, unwrapAnonymous } from '@/lib/api'
import type { Schemas } from '@/lib/api'
import { formErrors } from '@/lib/forms'
import { setSession } from '@/lib/session'

const router = useRouter()

// Whether registration is open decides between the form and the 'switched off' state.
const { data: instance, error, loading, reload } = useInstance()

const form = reactive<Schemas['RegisterRequest']>({ Email: '', Password: '', ConfirmPassword: '' })

// Registering signs the new user in.
const register = useMutation(async () => {
  setSession(await unwrapAnonymous(api.POST('/api/v1/account', { body: form })))
})

const errors = computed(() => formErrors(register.error.value, ['Email', 'Password', 'ConfirmPassword']))

async function submit(): Promise<boolean> {
  if (!(await register.run())) {
    return false
  }

  await router.replace({ name: 'apps' })
  return true
}

const linkClass = 'rounded-sm text-link underline-offset-4 hover:underline focus-visible:outline-2 focus-visible:outline-ring'
</script>

<template>
  <LoadingState v-if="loading" label="Loading" />

  <ErrorState v-else-if="error" :error="error" @retry="reload" />

  <div v-else-if="!instance?.AllowRegister" class="text-center">
    <h1 class="text-base font-medium">Registration is switched off</h1>
    <p class="mt-2 text-sm text-muted-foreground">
      New users cannot sign up on this instance. Ask an administrator of this instance for an account.
    </p>
    <Button as-child class="mt-6 w-full">
      <RouterLink :to="{ name: 'login' }">Sign in</RouterLink>
    </Button>
  </div>

  <AuthForm v-else title="Sign up" submit-label="Sign up" :submit="submit" :error="errors.message">
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

    <FormField v-slot="{ field }" label="Password" help="At least 8 characters." :error="errors.fields.Password">
      <Input v-model="form.Password" v-bind="field" type="password" name="new-password" autocomplete="new-password" />
    </FormField>

    <FormField v-slot="{ field }" label="Confirm password" :error="errors.fields.ConfirmPassword">
      <Input
        v-model="form.ConfirmPassword"
        v-bind="field"
        type="password"
        name="confirm-password"
        autocomplete="new-password"
      />
    </FormField>

    <template #footer>
      <p class="text-center text-sm text-muted-foreground">
        Already have an account?
        <RouterLink :to="{ name: 'login' }" :class="linkClass">Sign in</RouterLink>
      </p>
    </template>
  </AuthForm>
</template>
