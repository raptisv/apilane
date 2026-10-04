<script setup lang="ts">
import { CheckIcon, CircleAlertIcon, Loader2Icon, MailIcon, PencilIcon, RefreshCwIcon, TriangleAlertIcon, XIcon } from '@lucide/vue'
import { computed, nextTick, reactive, ref, useTemplateRef, watch } from 'vue'
import ErrorState from '@/components/ErrorState.vue'
import FormField from '@/components/FormField.vue'
import LoadingState from '@/components/LoadingState.vue'
import PageHeader from '@/components/PageHeader.vue'
import SecretInput from '@/components/SecretInput.vue'
import StateMessage from '@/components/StateMessage.vue'
import { Alert, AlertDescription, AlertTitle } from '@/components/ui/alert'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { Skeleton } from '@/components/ui/skeleton'
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@/components/ui/table'
import { useApplication } from '@/composables/useApplication'
import { useAsync } from '@/composables/useAsync'
import { useMutation } from '@/composables/useMutation'
import { api, unwrap } from '@/lib/api'
import { apiServer } from '@/lib/apiServer'
import { visibleTemplates } from '@/lib/emailTemplates'
import type { EmailTemplate } from '@/lib/emailTemplates'
import { formErrors } from '@/lib/forms'
import * as toast from '@/lib/toast'
import EmailTemplateDialog from './EmailTemplateDialog.vue'

const { application } = useApplication()

// AppLayout gives every application a fresh screen, so the token is fixed for the life of this one.
const appToken = application.value.Token

// Settings: SMTP and the landing page after the confirmation, one form saved together (as the
// classic page). They live in the Portal.
const { data, error, loading, reload } = useAsync(() =>
  unwrap(api.GET('/api/v1/applications/{appToken}/email-settings', { params: { path: { appToken } } })),
)

// The password starts empty: the API never sends it. Empty means 'keep the stored value'.
const form = reactive({
  MailServer: '',
  MailServerPort: '' as string | number,
  MailUserName: '',
  MailPassword: '',
  MailFromAddress: '',
  MailFromDisplayName: '',
  EmailConfirmationRedirectUrl: '',
})
const removeMailPassword = ref(false)

// Runs on the first load and after every save (the PUT answers the saved settings).
watch(data, (settings) => {
  if (!settings) {
    return
  }

  form.MailServer = settings.MailServer ?? ''
  form.MailServerPort = settings.MailServerPort ?? ''
  form.MailUserName = settings.MailUserName ?? ''
  form.MailPassword = ''
  form.MailFromAddress = settings.MailFromAddress ?? ''
  form.MailFromDisplayName = settings.MailFromDisplayName ?? ''
  form.EmailConfirmationRedirectUrl = settings.EmailConfirmationRedirectUrl ?? ''
  removeMailPassword.value = false
})

const save = useMutation(async () => {
  data.value = await unwrap(
    api.PUT('/api/v1/applications/{appToken}/email-settings', {
      params: { path: { appToken } },
      body: {
        MailServer: form.MailServer,
        MailServerPort: portText() === '' ? null : Number(portText()),
        MailUserName: form.MailUserName,
        // Null keeps the stored password; an empty string removes it.
        MailPassword: removeMailPassword.value ? '' : form.MailPassword || null,
        MailFromAddress: form.MailFromAddress,
        MailFromDisplayName: form.MailFromDisplayName,
        EmailConfirmationRedirectUrl: form.EmailConfirmationRedirectUrl,
      },
    }),
  )
})

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

  toast.success('Email settings saved.')
}

// Templates: read from the application's API server by the browser, as the classic page does. A
// failure shows in their section only; the settings above keep working.
const templates = useAsync(() =>
  apiServer(application.value.Server.ServerUrl, appToken).get<EmailTemplate[]>('/api/Email/GetEmails'),
)

const shownTemplates = computed(() => visibleTemplates(templates.data.value ?? []))

// `editing` keeps its value after the dialog closes, so the text does not change while it fades out.
const editOpen = ref(false)
const editing = ref<EmailTemplate>()

function openEdit(template: EmailTemplate): void {
  editing.value = template
  editOpen.value = true
}

const sectionClass = 'grid content-start gap-4 rounded-lg border bg-card p-4'
const noteClass = 'text-xs text-muted-foreground'
const linkClass = 'font-medium text-link underline underline-offset-4'
</script>

<template>
  <PageHeader
    title="Email"
    description="How this application sends its emails to its users: the mail server, the email templates and where a user lands after confirming their address."
  />

  <div class="grid gap-8">
    <LoadingState v-if="loading" label="Loading the email settings" :rows="6" />

    <ErrorState v-else-if="error" :error="error" @retry="reload" />

    <form v-else-if="data" ref="formEl" novalidate @submit.prevent="submit">
      <div class="grid gap-4 lg:grid-cols-2">
        <section
          :class="[sectionClass, data.IsMailSetup ? '' : 'border-warning/60']"
          aria-labelledby="smtp-title"
          :aria-describedby="data.IsMailSetup ? undefined : 'smtp-incomplete'"
        >
          <div>
            <h2 id="smtp-title" class="text-sm font-semibold">SMTP settings</h2>
            <p class="mt-1" :class="noteClass">The mail server this application sends its emails with.</p>
          </div>

          <div
            v-if="!data.IsMailSetup"
            id="smtp-incomplete"
            class="flex gap-2.5 rounded-lg border border-warning/40 bg-warning/10 p-3 text-sm text-warning"
          >
            <TriangleAlertIcon class="mt-0.5 size-4 shrink-0" aria-hidden="true" />
            <p>
              Incomplete: this application cannot send mail. Fill in the server, port, sender address, sender name,
              user name and password.
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

          <FormField v-slot="{ field }" label="Sender address" :error="errors.fields.MailFromAddress">
            <Input v-model="form.MailFromAddress" v-bind="field" type="email" autocomplete="off" spellcheck="false" />
          </FormField>

          <FormField v-slot="{ field }" label="Sender display name" :error="errors.fields.MailFromDisplayName">
            <Input v-model="form.MailFromDisplayName" v-bind="field" autocomplete="off" />
          </FormField>

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
        </section>

        <section :class="sectionClass" aria-labelledby="landing-title">
          <div>
            <h2 id="landing-title" class="text-sm font-semibold">Email confirmation landing page</h2>
            <p class="mt-1" :class="noteClass">After a user confirms their email address, they are sent to this URL.</p>
          </div>

          <FormField v-slot="{ field }" label="Redirect URL" :error="errors.fields.EmailConfirmationRedirectUrl">
            <Input
              v-model="form.EmailConfirmationRedirectUrl"
              v-bind="field"
              inputmode="url"
              autocomplete="off"
              spellcheck="false"
              maxlength="10000"
            />
          </FormField>

          <Alert role="note">
            <AlertDescription>
              <p>This setting applies only if the "Email confirmation" email is enabled.</p>
              <p>
                If you leave this field empty, the user is sent to this
                <RouterLink :to="{ name: 'email-confirmed' }" target="_blank" rel="noopener" :class="linkClass">
                  default landing page<span class="sr-only"> (opens in a new tab)</span></RouterLink
                >.
              </p>
            </AlertDescription>
          </Alert>
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

    <section class="grid content-start gap-4" aria-labelledby="templates-title">
      <div>
        <h2 id="templates-title" class="text-sm font-semibold">Email templates</h2>
        <p class="mt-1" :class="noteClass">
          The emails this application sends. They are stored on its API server, {{ application.Server.Name }}.
        </p>
      </div>

      <div v-if="templates.loading.value" role="status" class="space-y-2">
        <span class="sr-only">Loading the email templates</span>
        <Skeleton v-for="n in 2" :key="n" class="h-11 w-full" />
      </div>

      <Alert v-else-if="templates.error.value" variant="destructive">
        <CircleAlertIcon />
        <AlertTitle>Could not load the email templates from the API server</AlertTitle>
        <AlertDescription>
          <p>{{ templates.error.value.message }}</p>
          <Button variant="outline" size="sm" class="mt-3" @click="templates.reload()">
            <RefreshCwIcon />
            Try again
          </Button>
        </AlertDescription>
      </Alert>

      <StateMessage v-else-if="shownTemplates.length === 0" :icon="MailIcon" title="No email templates" />

      <div v-else class="overflow-hidden rounded-lg border bg-card">
        <Table>
          <TableHeader>
            <TableRow>
              <TableHead class="w-px pl-4">Enabled</TableHead>
              <TableHead>Description</TableHead>
              <TableHead class="pr-4"><span class="sr-only">Actions</span></TableHead>
            </TableRow>
          </TableHeader>
          <TableBody>
            <TableRow v-for="template in shownTemplates" :key="template.ID">
              <TableCell class="pl-4">
                <span v-if="template.Active" class="flex items-center text-success">
                  <CheckIcon class="size-4" aria-hidden="true" />
                  <span class="sr-only">Enabled</span>
                </span>
                <span v-else class="flex items-center text-destructive">
                  <XIcon class="size-4" aria-hidden="true" />
                  <span class="sr-only">Disabled</span>
                </span>
              </TableCell>
              <TableCell class="whitespace-normal">
                {{ template.Description }}
              </TableCell>
              <TableCell class="w-px pr-4">
                <div class="flex justify-end">
                  <Button variant="ghost" size="icon-sm" title="Edit" @click="openEdit(template)">
                    <PencilIcon />
                    <span class="sr-only">Edit {{ template.Description }}</span>
                  </Button>
                </div>
              </TableCell>
            </TableRow>
          </TableBody>
        </Table>
      </div>
    </section>
  </div>

  <EmailTemplateDialog v-if="editing" v-model:open="editOpen" :template="editing" @saved="templates.reload()" />
</template>
