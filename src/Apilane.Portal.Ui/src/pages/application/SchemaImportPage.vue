<script setup lang="ts">
import { CircleAlertIcon, CircleCheckIcon, CloudDownloadIcon, Loader2Icon, UploadIcon } from '@lucide/vue'
import { computed, nextTick, ref, shallowRef, useTemplateRef, watch } from 'vue'
import ApplicationSelect from '@/components/ApplicationSelect.vue'
import ConfirmDialog from '@/components/ConfirmDialog.vue'
import DiffCount from '@/components/diff/DiffCount.vue'
import FormField from '@/components/FormField.vue'
import LoadingState from '@/components/LoadingState.vue'
import PageHeader from '@/components/PageHeader.vue'
import SchemaImportHelp from '@/components/help/SchemaImportHelp.vue'
import { Alert, AlertDescription, AlertTitle } from '@/components/ui/alert'
import { Button } from '@/components/ui/button'
import { Textarea } from '@/components/ui/textarea'
import { useApplication } from '@/composables/useApplication'
import { useApplications } from '@/composables/useApplications'
import { useMutation } from '@/composables/useMutation'
import { api, unwrap } from '@/lib/api'
import { examplePayload, importFailure, parsePayload, payloadSummary } from '@/lib/schemaImport'
import type { SchemaImportPayload, SummaryCount } from '@/lib/schemaImport'

const { application, reload: reloadApplication } = useApplication()

// AppLayout gives every application a fresh screen, so the token is fixed for the life of this one.
const appToken = application.value.Token

// The applications a diff can be loaded from: the user's other ones. The rest of the screen works
// without them, so their loading and error states stay inside the 'Load from' card.
const { applications, error: applicationsError, loading: applicationsLoading, reload: reloadApplications } = useApplications()
const others = computed(() => (applications.value ?? []).filter((other) => other.Token !== appToken))

const payloadText = ref('')

// ---- Load diff ----

const sourceToken = ref<string>()
const sourceMissing = ref(false)

watch(sourceToken, () => (sourceMissing.value = false))

// What the last 'Load diff' found. No badges means the diff found nothing to import.
const loaded = shallowRef<{ source: string; summary: SummaryCount[] }>()

// useMutation does not hand back the answer.
let diff: SchemaImportPayload | undefined

// A read, but one a button starts: useMutation gives it the busy state and stops a second click.
const loadDiff = useMutation(async (source: string) => {
  diff = await unwrap(
    api.GET('/api/v1/applications/{appToken}/schema-import/diff', { params: { path: { appToken }, query: { Source: source } } }),
  )
})

async function onLoadDiff(): Promise<void> {
  const source = others.value.find((other) => other.Token === sourceToken.value)

  if (!source) {
    sourceMissing.value = true
    return
  }

  loaded.value = undefined

  if (!(await loadDiff.run(source.Token)) || !diff) {
    return
  }

  const summary = payloadSummary(diff)

  // Nothing missing leaves the payload box as it is.
  if (summary.length > 0) {
    payloadText.value = JSON.stringify(diff, null, 2)
  }

  loaded.value = { source: source.Name, summary }
}

// ---- Import ----

const payloadError = ref<string>()

watch(payloadText, () => (payloadError.value = undefined))

const confirmOpen = ref(false)
// The payload the confirm dialog was opened for, with the text it was read from.
let confirmed: { payload: SchemaImportPayload; text: string } | undefined

// The warnings of the last import that went through.
const warnings = shallowRef<string[]>()

// The text of the box is sent as written: writing the parsed object out
// again would round a Minimum or Maximum above 2^53.
const run = useMutation(async (payload: SchemaImportPayload, text: string) => {
  const result = await unwrap(
    api.POST('/api/v1/applications/{appToken}/schema-import', {
      params: { path: { appToken } },
      body: payload,
      bodySerializer: () => text,
    }),
  )

  warnings.value = result.Warnings
})

const failure = computed(() => (run.error.value ? importFailure(run.error.value) : undefined))

const formEl = useTemplateRef<HTMLFormElement>('formEl')
const resultEl = useTemplateRef<HTMLElement>('resultEl')

// The text is checked here, before any call: the API never sees text that is not a JSON object.
async function askImport(): Promise<void> {
  // A new attempt forgets the outcome of the last one, also when it is refused here.
  warnings.value = undefined
  run.reset()

  const parsed = parsePayload(payloadText.value)

  if (parsed.error !== undefined) {
    payloadError.value = parsed.error
    await nextTick()
    formEl.value?.querySelector<HTMLElement>('[aria-invalid="true"]')?.focus()
    return
  }

  confirmed = { payload: parsed.payload, text: payloadText.value }
  confirmOpen.value = true
}

// The outcome, a failure too, is shown on the screen, so the dialog always closes.
async function runImport(): Promise<boolean> {
  if (!confirmed) {
    return true
  }

  await run.run(confirmed.payload, confirmed.text)

  // A failed import may have applied its first steps, so the application is read again either way
  // (AppLayout shows the number of custom endpoints to the other screens).
  void reloadApplication()

  await nextTick()
  resultEl.value?.scrollIntoView({ block: 'nearest' })
  return true
}

const cardClass = 'grid gap-4 rounded-lg border bg-card p-4'
</script>

<template>
  <PageHeader
    title="Import schema"
    description="Adds entities, properties, constraints, security rules and custom endpoints to this application from a JSON payload."
  >
    <SchemaImportHelp :example="examplePayload" />
  </PageHeader>

  <div class="grid max-w-4xl min-w-0 content-start gap-6">
    <section aria-labelledby="source-title" :class="cardClass">
      <div>
        <h2 id="source-title" class="text-sm font-semibold">Load from another application</h2>
        <p class="mt-1 text-sm text-muted-foreground">
          Select an application to compute what it has that this application does not. The result is put into the JSON
          payload below, ready to import.
        </p>
      </div>

      <LoadingState v-if="applicationsLoading" label="Loading your applications" :rows="1" />

      <div
        v-else-if="applicationsError"
        role="alert"
        class="flex flex-wrap items-center gap-x-3 gap-y-1 text-sm text-destructive"
      >
        <span class="min-w-0 wrap-anywhere">Could not load your applications: {{ applicationsError.message }}</span>
        <Button type="button" variant="outline" size="sm" @click="reloadApplications">Try again</Button>
      </div>

      <p v-else-if="others.length === 0" class="text-sm text-muted-foreground">No other applications accessible.</p>

      <template v-else>
        <FormField
          v-slot="{ field }"
          label="Source application"
          :error="sourceMissing ? 'Select an application.' : undefined"
        >
          <div class="flex flex-wrap gap-2">
            <div class="min-w-0 flex-1 basis-56 sm:max-w-sm">
              <ApplicationSelect v-model="sourceToken" v-bind="field" :applications="others" />
            </div>
            <Button type="button" variant="outline" :disabled="loadDiff.pending.value" @click="onLoadDiff">
              <Loader2Icon v-if="loadDiff.pending.value" class="animate-spin" />
              <CloudDownloadIcon v-else />
              {{ loadDiff.pending.value ? 'Loading…' : 'Load diff' }}
            </Button>
          </div>
        </FormField>

        <Alert v-if="loadDiff.error.value" variant="destructive">
          <CircleAlertIcon />
          <AlertTitle>Could not load the diff</AlertTitle>
          <AlertDescription>{{ loadDiff.error.value.message }}</AlertDescription>
        </Alert>

        <div v-else-if="loaded" role="status" class="grid gap-2 text-sm">
          <p v-if="loaded.summary.length === 0" class="text-muted-foreground">
            No elements missing to import from <span class="wrap-anywhere">{{ loaded.source }}</span>.
          </p>
          <template v-else>
            <ul class="flex flex-wrap gap-1.5" aria-label="What the diff holds">
              <li v-for="item in loaded.summary" :key="item.label">
                <DiffCount tone="added" :count="item.count" :label="item.label" />
              </li>
            </ul>
            <p class="text-muted-foreground">
              The diff from <span class="wrap-anywhere">{{ loaded.source }}</span> is in the payload box. Review it and click
              Import when ready.
            </p>
          </template>
        </div>
      </template>
    </section>

    <form ref="formEl" :class="cardClass" novalidate @submit.prevent="askImport">
      <FormField
        v-slot="{ field }"
        label="JSON payload"
        help="Paste a payload, write one, or load a diff above. It can be edited before it is imported."
        :error="payloadError"
      >
        <Textarea
          v-model="payloadText"
          v-bind="field"
          class="h-96 field-sizing-fixed font-mono md:text-xs"
          spellcheck="false"
          autocomplete="off"
          placeholder='{"Entities": [], "Security": [], "CustomEndpoints": []}'
        />
      </FormField>

      <div ref="resultEl" class="empty:hidden">
        <Alert v-if="warnings" class="border-success/40">
          <CircleCheckIcon class="text-success" />
          <AlertTitle>Imported</AlertTitle>
          <AlertDescription>
            <p v-if="warnings.length === 0">Everything in the payload was added.</p>
            <template v-else>
              <p>These items were already there and were skipped:</p>
              <ul class="mt-1.5 grid list-disc gap-1 pl-5">
                <li v-for="(warning, index) in warnings" :key="index" class="wrap-anywhere">{{ warning }}</li>
              </ul>
            </template>
          </AlertDescription>
        </Alert>

        <Alert v-else-if="failure" variant="destructive">
          <CircleAlertIcon />
          <AlertTitle>Import failed</AlertTitle>
          <AlertDescription>
            <p class="wrap-anywhere">{{ failure.message }}</p>
            <ul v-if="failure.places.length > 0" class="mt-1.5 grid list-disc gap-1 pl-5">
              <li v-for="(place, index) in failure.places" :key="index" class="wrap-anywhere">
                <span class="font-mono text-xs">{{ place.place }}</span>
                <template v-if="place.message">: {{ place.message }}</template>
              </li>
            </ul>
            <p v-if="failure.traceId" class="mt-1.5 font-mono text-xs opacity-80">Trace {{ failure.traceId }}</p>
          </AlertDescription>
        </Alert>
      </div>

      <div class="flex justify-end">
        <Button type="submit" :disabled="run.pending.value">
          <Loader2Icon v-if="run.pending.value" class="animate-spin" />
          <UploadIcon v-else />
          Import
        </Button>
      </div>
    </form>
  </div>

  <ConfirmDialog
    v-model:open="confirmOpen"
    :title="`Import into ${application.Name}?`"
    description="The items of the payload are added one by one. This cannot be undone, and it is not atomic: if a step fails, the steps before it stay applied."
    confirm-label="Import"
    :action="runImport"
  />
</template>
