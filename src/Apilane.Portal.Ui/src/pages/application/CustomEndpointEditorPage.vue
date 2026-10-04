<script setup lang="ts">
import { ArrowLeftIcon, CircleAlertIcon, CompassIcon, InfoIcon, TriangleAlertIcon } from '@lucide/vue'
import { computed, nextTick, reactive, ref, useTemplateRef, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import ErrorState from '@/components/ErrorState.vue'
import FormField from '@/components/FormField.vue'
import LoadingState from '@/components/LoadingState.vue'
import PageHeader from '@/components/PageHeader.vue'
import SqlEditor from '@/components/SqlEditor.vue'
import StateMessage from '@/components/StateMessage.vue'
import UnsavedChangesBar from '@/components/UnsavedChangesBar.vue'
import { Alert, AlertDescription } from '@/components/ui/alert'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { useApplication } from '@/composables/useApplication'
import { useAsync } from '@/composables/useAsync'
import { useMutation } from '@/composables/useMutation'
import { api, ApiError, unwrap } from '@/lib/api'
import { renameLosesRules } from '@/lib/customEndpoints'
import { formErrors } from '@/lib/forms'
import type { SqlSchema } from '@/lib/sqlEditor'
import * as toast from '@/lib/toast'
import CustomEndpointTestPanel from './CustomEndpointTestPanel.vue'

// Creates a custom endpoint (/endpoints/new) or edits one (/endpoints/<id>): name, description and
// SQL on the left, the address, parameters and the SQL test on the right, the help below. Saving
// goes back to the list. A jump in the browser history from one editor
// address of this application to another keeps this screen, so the endpoint is read from the
// address each time it changes.
const route = useRoute()
const router = useRouter()
const { application, reload: reloadApplication } = useApplication()

// AppLayout gives every application a fresh screen, so the token is fixed for the life of this one.
const appToken = application.value.Token
const endpointId = computed(() => (route.name === 'app-endpoint-edit' ? Number(route.params.id) : undefined))

interface Values {
  Name: string
  Description: string
  Query: string
}

const empty: Values = { Name: '', Description: '', Query: '' }

const { data, error, loading, reload } = useAsync(
  () => {
    const id = endpointId.value

    return id === undefined
      ? Promise.resolve(undefined)
      : unwrap(api.GET('/api/v1/applications/{appToken}/custom-endpoints/{id}', { params: { path: { appToken, id } } }))
  },
  { watch: endpointId },
)

const notFound = computed(() => error.value instanceof ApiError && error.value.status === 404)
const ready = computed(() => endpointId.value === undefined || data.value !== undefined)

// What is stored (empty for a new endpoint) and what the form holds.
const saved = ref<Values>({ ...empty })
const form = reactive<Values>({ ...empty })

watch(data, (endpoint) => {
  if (endpoint) {
    saved.value = { Name: endpoint.Name, Description: endpoint.Description ?? '', Query: endpoint.Query }
    Object.assign(form, saved.value)
  }
})

const dirty = computed(() => form.Name !== saved.value.Name || form.Description !== saved.value.Description || form.Query !== saved.value.Query)

// The names offered while typing SQL. Without them the editor still works, so a failure only says so.
const schema = useAsync<SqlSchema>(async () => {
  const entities = (
    await unwrap(api.GET('/api/v1/applications/{appToken}/entities', { params: { path: { appToken }, query: { IncludeProperties: true } } }))
  ).Data

  return Object.fromEntries(entities.map((entity) => [entity.Name, (entity.Properties ?? []).map((property) => property.Name)]))
})

const save = useMutation(() => {
  const body = { Name: form.Name, Description: form.Description, Query: form.Query }
  const id = endpointId.value

  return id === undefined
    ? unwrap(api.POST('/api/v1/applications/{appToken}/custom-endpoints', { params: { path: { appToken } }, body }))
    : unwrap(api.PUT('/api/v1/applications/{appToken}/custom-endpoints/{id}', { params: { path: { appToken, id } }, body }))
})

// Another endpoint (or none, for /endpoints/new) starts from an empty form: the values of the one
// shown before must not stay while the new one loads. The leave guard has already asked.
watch(endpointId, () => {
  saved.value = { ...empty }
  Object.assign(form, empty)
  save.reset()
})

const errors = computed(() => formErrors(save.error.value, ['Name', 'Description', 'Query']))
const formEl = useTemplateRef<HTMLFormElement>('formEl')

async function submit(): Promise<void> {
  if (!(await save.run())) {
    // Move to the first rejected field, so it is read out and scrolled into view. An error of the
    // whole form (a taken name, a network failure) has no field: bring its message into view, as
    // Save is in the bar at the bottom and the message is at the top of the form.
    await nextTick()
    const invalid = formEl.value?.querySelector<HTMLElement>('[aria-invalid="true"]')
    if (invalid) {
      invalid.focus()
    } else {
      formEl.value?.querySelector('[data-slot="alert"]')?.scrollIntoView({ block: 'nearest' })
    }
    return
  }

  toast.success(endpointId.value === undefined ? 'Custom endpoint created.' : 'Custom endpoint saved.')
  // Nothing is unsaved any more, so the leave guard lets the screen go.
  saved.value = { ...form }

  if (endpointId.value === undefined) {
    // The number of custom endpoints is part of the application (the rename warnings of entities read it).
    void reloadApplication()
  }

  await router.push({ name: 'app-endpoints', params: { appToken } })
}

function discard(): void {
  Object.assign(form, saved.value)
  save.reset()
}

const renaming = computed(() => endpointId.value !== undefined && renameLosesRules(saved.value.Name, form.Name))

const linkClass = 'font-medium underline underline-offset-4'
const faqClass = 'rounded-lg border bg-card'
const faqSummaryClass =
  'cursor-pointer rounded-lg px-4 py-3 text-sm font-medium focus-visible:outline-2 focus-visible:-outline-offset-2 focus-visible:outline-ring'
const faqBodyClass = 'space-y-2 px-4 pb-4 text-sm text-muted-foreground'
</script>

<template>
  <RouterLink
    :to="{ name: 'app-endpoints', params: { appToken } }"
    class="mb-3 inline-flex items-center gap-1.5 rounded-sm text-sm text-muted-foreground underline-offset-4 hover:text-foreground hover:underline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring"
  >
    <ArrowLeftIcon class="size-3.5" aria-hidden="true" />
    Custom endpoints
  </RouterLink>

  <LoadingState v-if="loading" label="Loading the custom endpoint" />

  <StateMessage
    v-else-if="notFound"
    :icon="CompassIcon"
    as="h1"
    title="Custom endpoint not found"
    description="This application has no custom endpoint at this address. It may have been deleted."
  >
    <Button as-child>
      <RouterLink :to="{ name: 'app-endpoints', params: { appToken } }">Go to custom endpoints</RouterLink>
    </Button>
  </StateMessage>

  <ErrorState v-else-if="error" :error="error" @retry="reload" />

  <template v-else-if="ready">
    <PageHeader
      :title="endpointId === undefined ? 'New custom endpoint' : `Edit ${saved.Name}`"
      description="A SQL query that the API server runs when its address is called."
      class="wrap-anywhere"
    />

    <!--
      On a wide screen the result panel sits on the right of the form and the questions; on a narrow
      one it comes right after the form, before the questions.
    -->
    <div class="grid gap-6 lg:grid-cols-[minmax(0,2fr)_minmax(0,1fr)] lg:items-start">
      <form ref="formEl" class="grid min-w-0 gap-4 rounded-lg border bg-card p-4 lg:col-start-1" aria-label="Custom endpoint" novalidate @submit.prevent="submit">
        <!-- scroll-mt-16: the sticky header of narrow screens does not cover it. -->
        <Alert v-if="errors.message" variant="destructive" role="alert" class="scroll-mt-16">
          <CircleAlertIcon />
          <AlertDescription>{{ errors.message }}</AlertDescription>
        </Alert>

        <FormField
          v-slot="{ field }"
          label="Name"
          help="Letters a-z and A-Z only, at most 80. The address of the endpoint ends in its name."
          :error="errors.fields.Name"
        >
          <Input v-model="form.Name" v-bind="field" autocomplete="off" spellcheck="false" maxlength="80" />
        </FormField>

        <div
          v-if="renaming"
          class="flex gap-2.5 rounded-lg border border-warning/40 bg-warning/10 p-3 text-sm text-warning"
          role="status"
        >
          <TriangleAlertIcon class="mt-0.5 size-4 shrink-0" aria-hidden="true" />
          <p>
            Renaming changes the address of the endpoint, and the security rules written for {{ saved.Name }} no longer
            apply to it. After saving, give access again in the
            <RouterLink :to="{ name: 'app-security', params: { appToken } }" target="_blank" :class="linkClass">
              security section<span class="sr-only"> (opens in a new tab)</span></RouterLink
            >.
          </p>
        </div>

        <FormField v-slot="{ field }" label="Description (optional)" help="Why the endpoint exists." :error="errors.fields.Description">
          <Input v-model="form.Description" v-bind="field" autocomplete="off" />
        </FormField>

        <FormField v-slot="{ field }" label="SQL" :error="errors.fields.Query">
          <SqlEditor
            v-model="form.Query"
            v-bind="field"
            label="SQL"
            placeholder="SQL query"
            :schema="schema.data.value"
            :database-type="application.DatabaseType"
          />
        </FormField>

        <p v-if="schema.error.value" class="-mt-2 flex flex-wrap items-center gap-x-2 gap-y-1 text-xs text-muted-foreground">
          <span>The names of the entities and properties could not be loaded, so they are not offered while typing.</span>
          <Button type="button" variant="outline" size="xs" @click="schema.reload">Try again</Button>
        </p>

        <ul class="list-disc space-y-1.5 pl-5 text-sm text-muted-foreground">
          <li>
            Type the SQL query to run when this endpoint is called. Add query string parameters in braces: a parameter
            named ProductID is typed as <code class="font-mono text-foreground">{ProductID}</code>.
          </li>
          <li>
            <strong class="font-medium text-foreground">Query string parameters can only be long integers.</strong> If a
            parameter is not a long integer, its placeholder is replaced with <code class="font-mono text-foreground">null</code>.
          </li>
          <li>
            Use the keyword <code class="font-mono text-foreground">{Owner}</code> to have it replaced with the ID of the user
            who made the call. An endpoint that uses {Owner} can only be called by signed-in users.
          </li>
        </ul>

        <div
          v-if="endpointId === undefined"
          class="flex gap-2.5 rounded-lg border border-primary/40 bg-primary/10 p-3 text-sm"
        >
          <InfoIcon class="mt-0.5 size-4 shrink-0 text-link" aria-hidden="true" />
          <p>
            Do not forget to give users access to the custom endpoint in the
            <RouterLink :to="{ name: 'app-security', params: { appToken } }" target="_blank" :class="[linkClass, 'text-link']">
              security section<span class="sr-only"> (opens in a new tab)</span></RouterLink
            >.
          </p>
        </div>
      </form>

      <CustomEndpointTestPanel
        class="min-w-0 lg:sticky lg:top-4 lg:col-start-2 lg:row-span-2 lg:row-start-1"
        :server-url="application.Server.ServerUrl"
        :app-token="appToken"
        :name="form.Name"
        :query="form.Query"
      />

      <section aria-labelledby="faq-title" class="grid min-w-0 gap-2 lg:col-start-1">
        <h2 id="faq-title" class="text-sm font-semibold">Questions</h2>

        <details :class="faqClass">
          <summary :class="faqSummaryClass">What is a custom endpoint?</summary>
          <div :class="faqBodyClass">
            <p>Custom endpoints let you get, create, update or delete records, your way.</p>
            <p>Custom endpoints accept only numeric parameters.</p>
            <p>Use custom endpoints to join tables (entities) and get the results exactly the way you like.</p>
            <p class="font-medium text-foreground">
              Custom endpoint queries run inside a transaction. If the query fails, the transaction is not committed.
            </p>
          </div>
        </details>

        <details :class="faqClass">
          <summary :class="faqSummaryClass">How not to use custom endpoints</summary>
          <div :class="faqBodyClass">
            <ul class="list-disc space-y-1.5 pl-5">
              <li>
                Do not use custom endpoints to create or drop tables or properties. If that happens, you will have to
                <RouterLink :to="{ name: 'app-settings', params: { appToken } }" :class="linkClass">rebuild</RouterLink>
                your application. Rebuilding the application leads to data loss.
              </li>
              <li>
                Do not use custom endpoints to create, update or delete records that need specific validation. Do it only
                if you know exactly what you are doing.
              </li>
            </ul>
          </div>
        </details>

        <details :class="faqClass">
          <summary :class="faqSummaryClass">How do I create a custom endpoint?</summary>
          <div :class="faqBodyClass">
            <ul class="list-disc space-y-1 pl-5">
              <li>Pick the name you like.</li>
              <li>Write a description to help you remember why you created this endpoint.</li>
              <li>Write the SQL query you want to run every time the endpoint is called.</li>
              <li>Save.</li>
              <li>Give access to the user roles you like, in the security section.</li>
              <li>You are all set.</li>
            </ul>
          </div>
        </details>

        <details :class="faqClass">
          <summary :class="faqSummaryClass">Can I change the name of a custom endpoint later?</summary>
          <div :class="faqBodyClass">
            <p>
              Yes, at any time. Security access is given by name, so after a rename the endpoint has no access until you
              give it again.
            </p>
          </div>
        </details>

        <details v-if="application.DatabaseType === 'SQLServer'" :class="faqClass">
          <summary :class="faqSummaryClass">How can I raise a custom SQL error?</summary>
          <div :class="faqBodyClass">
            <p>Use <code class="font-mono text-foreground">RAISERROR('Your error message', 16, 1);</code></p>
          </div>
        </details>
      </section>
    </div>

    <UnsavedChangesBar
      :dirty="dirty"
      :pending="save.pending.value"
      :message="endpointId === undefined ? 'The new custom endpoint is not saved yet.' : 'The custom endpoint has unsaved changes.'"
      @save="submit"
      @discard="discard"
    />
  </template>
</template>
