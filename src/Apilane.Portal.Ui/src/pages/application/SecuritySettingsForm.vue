<script setup lang="ts">
import { CircleAlertIcon, ExternalLinkIcon, Loader2Icon, TriangleAlertIcon } from '@lucide/vue'
import { computed, nextTick, reactive, ref, useTemplateRef, watch } from 'vue'
import ConfirmDialog from '@/components/ConfirmDialog.vue'
import CopyField from '@/components/CopyField.vue'
import FormField from '@/components/FormField.vue'
import SwitchField from '@/components/SwitchField.vue'
import TextListInput from '@/components/TextListInput.vue'
import { Alert, AlertDescription } from '@/components/ui/alert'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'
import { useLeaveGuard } from '@/composables/useLeaveGuard'
import { useMutation } from '@/composables/useMutation'
import { api, unwrap } from '@/lib/api'
import type { Schemas } from '@/lib/api'
import { formErrors, listErrors } from '@/lib/forms'
import * as toast from '@/lib/toast'

// The access settings of the security screen: sign-in, register, files, IP access, and the two
// forgot-password links (read-only). One form with its own Save; the rules below save separately.
const props = defineProps<{
  appToken: string
  settings: Schemas['SecuritySettingsResponse']
  links: Schemas['ForgotPasswordLinksResponse']
  /** True while the rules editor on the same screen holds unsaved rules: its bar then asks before leaving. */
  rulesDirty: boolean
}>()

const emit = defineEmits<{ saved: [settings: Schemas['SecuritySettingsResponse']] }>()

const form = reactive({
  AuthTokenExpireMinutes: '' as string | number,
  ForceSingleLogin: false,
  AllowLoginUnconfirmedEmail: false,
  AllowUserRegister: false,
  MaxAllowedFileSizeInKB: '' as string | number,
  ClientIPsLogic: 'Block',
  ClientIPs: [] as string[],
})

// Runs on the first load and after every save (the PUT answers the saved settings).
watch(
  () => props.settings,
  (settings) => {
    form.AuthTokenExpireMinutes = settings.AuthTokenExpireMinutes
    form.ForceSingleLogin = settings.ForceSingleLogin
    form.AllowLoginUnconfirmedEmail = settings.AllowLoginUnconfirmedEmail
    form.AllowUserRegister = settings.AllowUserRegister
    form.MaxAllowedFileSizeInKB = settings.MaxAllowedFileSizeInKB
    form.ClientIPsLogic = settings.ClientIPsLogic
    form.ClientIPs = [...settings.ClientIPs]
  },
  { immediate: true },
)

// True while the form differs from the saved settings. The Save of the rules below does not save
// these, so the form says so and asks before the screen is left.
const dirty = computed(() => {
  const saved = props.settings

  return (
    Number(form.AuthTokenExpireMinutes) !== saved.AuthTokenExpireMinutes ||
    form.ForceSingleLogin !== saved.ForceSingleLogin ||
    form.AllowLoginUnconfirmedEmail !== saved.AllowLoginUnconfirmedEmail ||
    form.AllowUserRegister !== saved.AllowUserRegister ||
    Number(form.MaxAllowedFileSizeInKB) !== saved.MaxAllowedFileSizeInKB ||
    form.ClientIPsLogic !== saved.ClientIPsLogic ||
    form.ClientIPs.length !== saved.ClientIPs.length ||
    form.ClientIPs.some((address, index) => address !== saved.ClientIPs[index])
  )
})

// One question at a time: with unsaved rules the bar of the rules editor asks for the whole screen.
// Two guards that both asked would open each other's question again and again.
const { open: leaveOpen, leave } = useLeaveGuard(() => dirty.value && !props.rulesDirty)

const save = useMutation(async () => {
  const saved = await unwrap(
    api.PUT('/api/v1/applications/{appToken}/security/settings', {
      params: { path: { appToken: props.appToken } },
      body: {
        AuthTokenExpireMinutes: Number(form.AuthTokenExpireMinutes),
        ForceSingleLogin: form.ForceSingleLogin,
        AllowLoginUnconfirmedEmail: form.AllowLoginUnconfirmedEmail,
        AllowUserRegister: form.AllowUserRegister,
        MaxAllowedFileSizeInKB: Number(form.MaxAllowedFileSizeInKB),
        ClientIPsLogic: form.ClientIPsLogic,
        ClientIPs: form.ClientIPs,
      },
    }),
  )
  emit('saved', saved)
})

// Checked here: an empty or broken number box would reach the API as something it cannot read.
const numberErrors = ref<Record<string, string>>({})

function checkNumbers(): boolean {
  const errors: Record<string, string> = {}

  for (const name of ['AuthTokenExpireMinutes', 'MaxAllowedFileSizeInKB'] as const) {
    if (!Number.isInteger(form[name])) {
      errors[name] = 'Enter a whole number.'
    }
  }

  numberErrors.value = errors
  return Object.keys(errors).length === 0
}

const errors = computed(() => {
  // The addresses are named by place (ClientIPs[2]); they show at their entry, not above the form.
  const names = [...Object.keys(form), ...form.ClientIPs.map((_, index) => `ClientIPs[${index}]`)]
  const result = formErrors(save.error.value, names)
  Object.assign(result.fields, numberErrors.value)
  return result
})

const addressErrors = computed(() => listErrors(save.error.value, 'ClientIPs').rows)

// Adding or removing an address moves the places the messages point at.
watch(
  () => form.ClientIPs.length,
  () => save.reset(),
)

const formEl = useTemplateRef<HTMLFormElement>('formEl')

async function submit(): Promise<void> {
  if (!checkNumbers() || !(await save.run())) {
    // Move to the first rejected field, so it is read out and scrolled into view.
    await nextTick()
    formEl.value?.querySelector<HTMLElement>('[aria-invalid="true"]')?.focus()
    return
  }

  toast.success('Security settings saved.')
}

const allowOnly = computed(() => form.ClientIPsLogic === 'Allow' && form.ClientIPs.some((address) => address.trim() !== ''))

const sectionClass = 'grid content-start gap-4 rounded-lg border bg-card p-4'
const noteClass = 'text-xs text-muted-foreground'
const linkClass = 'font-medium text-link underline underline-offset-4'
</script>

<template>
  <form ref="formEl" novalidate aria-label="Security settings" class="min-w-0" @submit.prevent="submit">
    <div class="grid gap-4 lg:grid-cols-2">
      <section :class="sectionClass" aria-labelledby="signin-title">
        <h2 id="signin-title" class="text-sm font-semibold">Sign-in</h2>

        <FormField
          v-slot="{ field }"
          label="Auth token lifetime (minutes)"
          help="When a user signs in, the auth token stays valid for at least this many minutes. After that time of inactivity, the user has to sign in again."
          :error="errors.fields.AuthTokenExpireMinutes"
        >
          <Input v-model="form.AuthTokenExpireMinutes" v-bind="field" type="number" min="1" step="1" class="max-w-40" />
        </FormField>

        <SwitchField
          v-model="form.ForceSingleLogin"
          label="Allow only one sign-in at a time"
          help="A new sign-in ends the user's other sessions."
          :error="errors.fields.ForceSingleLogin"
        />

        <SwitchField
          v-model="form.AllowLoginUnconfirmedEmail"
          label="Allow users with an unconfirmed email to sign in"
          :error="errors.fields.AllowLoginUnconfirmedEmail"
        />
      </section>

      <section :class="sectionClass" aria-labelledby="register-title">
        <h2 id="register-title" class="text-sm font-semibold">Register</h2>

        <SwitchField v-model="form.AllowUserRegister" label="Allow new users to register" :error="errors.fields.AllowUserRegister" />
      </section>

      <section :class="sectionClass" aria-labelledby="files-title">
        <h2 id="files-title" class="text-sm font-semibold">Files</h2>

        <FormField
          v-slot="{ field }"
          label="Maximum file size (KB)"
          help="The largest file a user can upload: 1 to 25600 KB (25 MB)."
          :error="errors.fields.MaxAllowedFileSizeInKB"
        >
          <Input v-model="form.MaxAllowedFileSizeInKB" v-bind="field" type="number" min="1" max="25600" step="1" class="max-w-40" />
        </FormField>
      </section>

      <section :class="sectionClass" aria-labelledby="ip-title">
        <div>
          <h2 id="ip-title" class="text-sm font-semibold">IP access</h2>
          <p class="mt-1" :class="noteClass">
            Which IPv4 addresses may call the API of this application. An address is compared exactly with the caller's.
            Leave the list empty to accept every address.
          </p>
        </div>

        <FormField v-slot="{ field }" label="Addresses in the list are" :error="errors.fields.ClientIPsLogic">
          <Select v-model="form.ClientIPsLogic">
            <SelectTrigger v-bind="field" class="w-full sm:w-auto">
              <SelectValue />
            </SelectTrigger>
            <SelectContent>
              <SelectItem value="Block">Blocked: every other address is accepted</SelectItem>
              <SelectItem value="Allow">Allowed: every other address is blocked</SelectItem>
            </SelectContent>
          </Select>
        </FormField>

        <TextListInput
          v-model="form.ClientIPs"
          item-label="IP address"
          add-label="Add address"
          placeholder="192.168.0.1"
          :errors="addressErrors"
        />

        <div v-if="allowOnly" class="flex gap-2.5 rounded-lg border border-warning/40 bg-warning/10 p-3 text-sm text-warning">
          <TriangleAlertIcon class="mt-0.5 size-4 shrink-0" aria-hidden="true" />
          <p>Only the addresses in the list can call the API. Check that the addresses you call it from are there.</p>
        </div>
      </section>

      <section :class="sectionClass" class="lg:col-span-2" aria-labelledby="forgot-title">
        <div>
          <h2 id="forgot-title" class="text-sm font-semibold">Forgot password</h2>
          <p class="mt-1" :class="noteClass">
            Both send the password-reset email, which needs the
            <RouterLink :to="{ name: 'app-email', params: { appToken } }" :class="linkClass">SMTP settings</RouterLink>
            of this application.
          </p>
        </div>

        <div class="grid gap-1.5">
          <CopyField label="When a user has forgotten their password, send them to this page" :value="links.PageUrl" />
          <a :href="links.PageUrl" target="_blank" rel="noopener" class="inline-flex w-fit items-center gap-1 text-xs" :class="linkClass">
            Open the page
            <ExternalLinkIcon class="size-3" aria-hidden="true" />
            <span class="sr-only">(opens in a new tab)</span>
          </a>
        </div>

        <CopyField label="Or call this API endpoint ({Email} is the user's address)" :value="links.ApiUrl" />
      </section>
    </div>

    <Alert v-if="errors.message" variant="destructive" role="alert" class="mt-4">
      <CircleAlertIcon />
      <AlertDescription>{{ errors.message }}</AlertDescription>
    </Alert>

    <div class="mt-4 flex items-center justify-end gap-3">
      <p v-if="dirty" role="status" class="text-sm text-muted-foreground">Not saved yet</p>
      <Button type="submit" :disabled="save.pending.value">
        <Loader2Icon v-if="save.pending.value" class="animate-spin" />
        Save settings
      </Button>
    </div>
  </form>

  <ConfirmDialog
    v-model:open="leaveOpen"
    title="Leave without saving?"
    description="Your changes on this screen are not saved. If you leave, they are lost."
    confirm-label="Leave"
    :action="leave"
  />
</template>
