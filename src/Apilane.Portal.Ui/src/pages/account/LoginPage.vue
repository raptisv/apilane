<script setup lang="ts">
import { computed, reactive } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import AuthForm from '@/components/AuthForm.vue'
import FormField from '@/components/FormField.vue'
import { Input } from '@/components/ui/input'
import { useInstance } from '@/composables/useInstance'
import { useMutation } from '@/composables/useMutation'
import { api, unwrapAnonymous } from '@/lib/api'
import type { Schemas } from '@/lib/api'
import { formErrors } from '@/lib/forms'
import { setSession } from '@/lib/session'
import { afterSignIn } from '@/router'

const route = useRoute()
const router = useRouter()

// Only for the 'Sign up' link: when it cannot be loaded, signing in still works.
const { data: instance } = useInstance()

const form = reactive<Schemas['SignInRequest']>({ Email: '', Password: '' })

const signIn = useMutation(async () => {
  setSession(await unwrapAnonymous(api.POST('/api/v1/session', { body: form })))
})

// A wrong e-mail and a wrong password give the same form-level message: 'Invalid login attempt.'
const errors = computed(() => formErrors(signIn.error.value, ['Email', 'Password']))

async function submit(): Promise<boolean> {
  if (!(await signIn.run())) {
    return false
  }

  const target = afterSignIn(route.query.returnUrl)

  // false: a full page load is under way, to an address the Portal answers itself (/swagger).
  if (target) {
    await router.replace(target)
  }

  return true
}

const linkClass = 'rounded-sm text-link underline-offset-4 hover:underline focus-visible:outline-2 focus-visible:outline-ring'
</script>

<template>
  <AuthForm title="Sign in" submit-label="Sign in" :submit="submit" :error="errors.message">
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

    <FormField v-slot="{ field }" label="Password" :error="errors.fields.Password">
      <Input v-model="form.Password" v-bind="field" type="password" name="password" autocomplete="current-password" />
    </FormField>

    <template #footer>
      <p class="text-center text-sm">
        <RouterLink :to="{ name: 'forgot-password' }" :class="linkClass">Forgot password?</RouterLink>
      </p>
      <p v-if="instance?.AllowRegister" class="text-center text-sm text-muted-foreground">
        Do not have an account?
        <RouterLink :to="{ name: 'register' }" :class="linkClass">Sign up</RouterLink>
      </p>
    </template>
  </AuthForm>
</template>
