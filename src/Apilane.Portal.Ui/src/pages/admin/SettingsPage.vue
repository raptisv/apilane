<script setup lang="ts">
import { CircleAlertIcon, DownloadIcon, Loader2Icon, TriangleAlertIcon } from '@lucide/vue'
import { computed, nextTick, reactive, ref, useTemplateRef, watch } from 'vue'
import { useRoute } from 'vue-router'
import ErrorState from '@/components/ErrorState.vue'
import FormField from '@/components/FormField.vue'
import LoadingState from '@/components/LoadingState.vue'
import PageHeader from '@/components/PageHeader.vue'
import SecretInput from '@/components/SecretInput.vue'
import SwitchField from '@/components/SwitchField.vue'
import { Alert, AlertDescription } from '@/components/ui/alert'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { useAsync } from '@/composables/useAsync'
import { useMutation } from '@/composables/useMutation'
import { api, unwrap } from '@/lib/api'
import { formErrors } from '@/lib/forms'
import { loadSession } from '@/lib/session'
import * as toast from '@/lib/toast'
import { setTitle } from '@/router'

const route = useRoute()

const { data, error, loading, reload } = useAsync(() => unwrap(api.GET('/api/v1/admin/settings')))

// The two secrets start empty: the API never sends them. Empty means 'keep the stored value'.
const form = reactive({
  InstanceTitle: '',
  AllowRegisterToPortal: false,
  InstallationKey: '',
  MailServer: '',
  MailServerPort: '' as string | number,
  MailUserName: '',
  MailPassword: '',
  MailFromAddress: '',
  MailFromDisplayName: '',
})
const removeMailPassword = ref(false)

// Runs on the first load and after every save.
watch(data, (settings) => {
  if (!settings) {
    return
  }

  form.InstanceTitle = settings.InstanceTitle
  form.AllowRegisterToPortal = settings.AllowRegisterToPortal
  form.InstallationKey = ''
  form.MailServer = settings.MailServer ?? ''
  form.MailServerPort = settings.MailServerPort ?? ''
  form.MailUserName = settings.MailUserName ?? ''
  form.MailPassword = ''
  form.MailFromAddress = settings.MailFromAddress ?? ''
  form.MailFromDisplayName = settings.MailFromDisplayName ?? ''
  removeMailPassword.value = false
})

const save = useMutation(() =>
  unwrap(
    api.PUT('/api/v1/admin/settings', {
      body: {
        InstanceTitle: form.InstanceTitle,
        AllowRegisterToPortal: form.AllowRegisterToPortal,
        // Null keeps the stored secret; an empty mail password removes it.
        InstallationKey: form.InstallationKey || null,
        MailServer: form.MailServer,
        MailServerPort: portText() === '' ? null : Number(portText()),
        MailUserName: form.MailUserName,
        MailPassword: removeMailPassword.value ? '' : form.MailPassword || null,
        MailFromAddress: form.MailFromAddress,
        MailFromDisplayName: form.MailFromDisplayName,
      },
    }),
  ),
)

// A text box, checked here: a number input reports text it cannot read as empty, which would save 'no port'.
const portError = ref<string>()

function portText(): string {
  return String(form.MailServerPort).trim()
}

const errors = computed(() => {
  const result = formErrors(save.error.value, Object.keys(form))

  if (portError.value) {
    result.fields.MailServerPort = portError.value
  }

  return result
})

const formEl = useTemplateRef<HTMLFormElement>('formEl')

async function submit(): Promise<void> {
  portError.value = portText() !== '' && !/^\d+$/.test(portText()) ? 'Must be a whole number.' : undefined

  if (portError.value || !(await save.run())) {
    // Move to the first rejected field, so it is read out and scrolled into view.
    await nextTick()
    formEl.value?.querySelector<HTMLElement>('[aria-invalid="true"]')?.focus()
    return
  }

  toast.success('Settings saved.')
  void reload()

  // The instance name is part of the session: load it again so the sidebar and the tab title follow.
  loadSession().then(
    () => setTitle(route.meta.title),
    () => {
      // The settings are saved; the old name just stays on screen until the next page load.
    },
  )
}

const sectionClass = 'grid content-start gap-4 rounded-lg border bg-card p-4'
const noteClass = 'text-xs text-muted-foreground'
</script>

<template>
  <PageHeader title="Settings" description="The settings of this Apilane instance." />

  <LoadingState v-if="loading" label="Loading settings" :rows="6" />

  <ErrorState v-else-if="error" :error="error" @retry="reload" />

  <template v-else-if="data">
    <form ref="formEl" novalidate @submit.prevent="submit">
      <div class="grid gap-4 lg:grid-cols-2">
        <section :class="sectionClass" aria-labelledby="general-title">
          <h2 id="general-title" class="text-sm font-semibold">General</h2>

          <FormField
            v-slot="{ field }"
            label="Instance name"
            help="3 to 16 characters. Internal use only: nobody outside this Portal sees the name."
            :error="errors.fields.InstanceTitle"
          >
            <Input v-model="form.InstanceTitle" v-bind="field" autocomplete="off" maxlength="16" />
          </FormField>

          <SwitchField
            v-model="form.AllowRegisterToPortal"
            label="Allow new users to register on this instance"
            help="If the instance can be reached from the internet, you probably want to keep strangers from creating an account. To let one person in, turn this on for a while and turn it off again after they have registered."
            :error="errors.fields.AllowRegisterToPortal"
          />

          <FormField v-slot="{ field }" label="Installation key" :error="errors.fields.InstallationKey">
            <SecretInput v-model="form.InstallationKey" v-bind="field" :is-set="data.HasInstallationKey" />
          </FormField>

          <div class="flex gap-2.5 rounded-lg border border-warning/40 bg-warning/10 p-3 text-sm text-warning">
            <TriangleAlertIcon class="mt-0.5 size-4 shrink-0" aria-hidden="true" />
            <p>
              Do not share this key. The Portal and the API instances use it to authenticate to each other. If you
              change it, you also have to re-instantiate the API instances with the new key. A new key is 30 to 100
              characters without spaces.
            </p>
          </div>
        </section>

        <section :class="sectionClass" aria-labelledby="mail-title">
          <div>
            <h2 id="mail-title" class="text-sm font-semibold">Mail</h2>
            <p class="mt-1" :class="noteClass">
              This instance uses these settings to send the mail for a forgotten password. They do not apply to any
              application: each application has its own mail settings.
            </p>
            <p v-if="!data.IsMailSetup" class="mt-1 text-xs text-warning">
              Not complete: until every field is filled in, this instance cannot send mail.
            </p>
          </div>

          <div class="grid gap-4 sm:grid-cols-[1fr_8rem]">
            <FormField v-slot="{ field }" label="Mail server" :error="errors.fields.MailServer">
              <Input v-model="form.MailServer" v-bind="field" autocomplete="off" spellcheck="false" />
            </FormField>

            <FormField v-slot="{ field }" label="Port" :error="errors.fields.MailServerPort">
              <Input v-model="form.MailServerPort" v-bind="field" type="text" inputmode="numeric" autocomplete="off" />
            </FormField>
          </div>

          <FormField v-slot="{ field }" label="User name" :error="errors.fields.MailUserName">
            <Input v-model="form.MailUserName" v-bind="field" autocomplete="off" spellcheck="false" />
          </FormField>

          <FormField v-slot="{ field }" label="Password" :error="errors.fields.MailPassword">
            <SecretInput
              v-model="form.MailPassword"
              v-model:remove="removeMailPassword"
              v-bind="field"
              :is-set="data.HasMailPassword"
              removable
            />
          </FormField>

          <FormField v-slot="{ field }" label="From address" :error="errors.fields.MailFromAddress">
            <Input v-model="form.MailFromAddress" v-bind="field" type="email" autocomplete="off" spellcheck="false" />
          </FormField>

          <FormField v-slot="{ field }" label="From display name" :error="errors.fields.MailFromDisplayName">
            <Input v-model="form.MailFromDisplayName" v-bind="field" autocomplete="off" />
          </FormField>
        </section>
      </div>

      <Alert v-if="errors.message" variant="destructive" role="alert" class="mt-4">
        <CircleAlertIcon />
        <AlertDescription>{{ errors.message }}</AlertDescription>
      </Alert>

      <div class="mt-4 flex justify-end">
        <Button type="submit" :disabled="save.pending.value">
          <Loader2Icon v-if="save.pending.value" class="animate-spin" />
          Save settings
        </Button>
      </div>
    </form>

    <section :class="sectionClass" class="mt-8" aria-labelledby="backup-title">
      <div>
        <h2 id="backup-title" class="text-sm font-semibold">Backup database</h2>
        <p class="mt-1" :class="noteClass">
          Downloads a copy of the Portal database as the file Apilane.db: users, applications, settings and the audit
          log. Every download is written to the audit log.
        </p>
      </div>

      <div class="flex gap-2.5 rounded-lg border border-destructive/40 bg-destructive/10 p-3 text-sm text-destructive">
        <TriangleAlertIcon class="mt-0.5 size-4 shrink-0" aria-hidden="true" />
        <p>
          The file contains every secret of this instance: password hashes, the installation key, and the connection
          strings, encryption keys and mail passwords of every application. Store it somewhere safe.
        </p>
      </div>

      <div>
        <!--
          A plain link, so the browser streams the file to disk instead of holding it in memory.
          No download attribute: the answer names the file itself, and a failed answer is shown instead of saved.
        -->
        <Button as-child variant="outline">
          <a href="/api/v1/admin/backup">
            <DownloadIcon />
            Download backup
          </a>
        </Button>
      </div>
    </section>
  </template>
</template>
