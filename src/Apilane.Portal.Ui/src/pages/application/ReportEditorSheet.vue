<script setup lang="ts">
import { CircleAlertIcon, Loader2Icon, PlusIcon } from '@lucide/vue'
import { computed, nextTick, ref, shallowRef, useId, useTemplateRef, watch } from 'vue'
import ErrorState from '@/components/ErrorState.vue'
import FormField from '@/components/FormField.vue'
import LoadingState from '@/components/LoadingState.vue'
import { Alert, AlertDescription } from '@/components/ui/alert'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'
import { Sheet, SheetContent, SheetDescription, SheetFooter, SheetHeader, SheetTitle } from '@/components/ui/sheet'
import { useAsync } from '@/composables/useAsync'
import { useMutation } from '@/composables/useMutation'
import { api, unwrap } from '@/lib/api'
import type { Report } from '@/lib/reportData'
import { newReportForm, newSeries, reportErrors, reportForm, reportRequest, reportTypes } from '@/lib/reports'
import type { ReportForm, SeriesForm } from '@/lib/reports'
import * as toast from '@/lib/toast'
import ReportSeriesEditor from './ReportSeriesEditor.vue'
import TimeRangePicker from './TimeRangePicker.vue'

// Creates or edits one report, in a side sheet over the dashboard: title, visualization, time
// range, Top N and the series. Problems the API reports show at the field or the series they are
// about. Where the panel sits is not edited here: it is dragged on the dashboard.
const props = defineProps<{
  appToken: string
  /** The report to edit; none for a new report. */
  report?: Report
}>()

const emit = defineEmits<{ saved: [] }>()

const open = defineModel<boolean>('open', { required: true })

// The entities with their properties: the choices of a series and of its filter.
const entities = useAsync(() =>
  unwrap(
    api.GET('/api/v1/applications/{appToken}/entities', {
      params: { path: { appToken: props.appToken }, query: { IncludeProperties: true } },
    }),
  ),
)

const entityList = computed(() => entities.data.value?.Data ?? [])

const form = ref<ReportForm>()
// A stable key per series row, so a row keeps its own state when another one is removed.
const rowKeys = ref<number[]>([])
let nextKey = 0
// Counts the openings: a new one gives every part of the form a fresh start.
const opening = ref(0)

// For each series of the last save, its place in the form (see reportRequest).
const sentRows = shallowRef<number[]>([])

const save = useMutation(async () => {
  if (!form.value) {
    return
  }

  const { body, rows } = reportRequest(form.value)

  sentRows.value = rows

  if (props.report) {
    await unwrap(
      api.PUT('/api/v1/applications/{appToken}/reports/{reportId}', {
        params: { path: { appToken: props.appToken, reportId: props.report.ID } },
        body,
      }),
    )
  } else {
    await unwrap(api.POST('/api/v1/applications/{appToken}/reports', { params: { path: { appToken: props.appToken } }, body }))
  }
})

const errors = computed(() => reportErrors(save.error.value, sentRows.value))

// Every opening starts from the stored report, or from an empty one on the first entity. The form
// of the last opening stays while the sheet slides out.
let filledFor: number | 'new' | undefined

watch(
  [open, entityList, () => props.report?.ID],
  ([isOpen, list, id]) => {
    if (!isOpen) {
      filledFor = undefined
      return
    }

    const target = id ?? 'new'

    if (entities.data.value && filledFor !== target) {
      filledFor = target
      form.value = props.report ? reportForm(props.report) : newReportForm(list[0]?.Name ?? '')
      rowKeys.value = form.value.Series.map(() => nextKey++)
      opening.value++
      save.reset()
    }
  },
  { immediate: true },
)

const formEl = useTemplateRef<HTMLFormElement>('formEl')
const seriesHeadingId = useId()

async function onSubmit(): Promise<void> {
  if (!form.value) {
    return
  }

  if (await save.run()) {
    toast.success(props.report ? 'Report saved.' : 'Report created.')
    emit('saved')
  } else {
    // Move to the first field the API rejected.
    await nextTick()
    formEl.value?.querySelector<HTMLElement>('[aria-invalid="true"]')?.focus()
  }
}

// Escape and the close button do nothing while the save is running.
function setOpen(value: boolean): void {
  if (!save.pending.value) {
    open.value = value
  }
}

function setSeries(index: number, value: SeriesForm): void {
  if (form.value) {
    form.value.Series[index] = value
  }
}

async function addSeries(): Promise<void> {
  if (!form.value) {
    return
  }

  form.value.Series.push(newSeries(entityList.value[0]?.Name ?? ''))
  rowKeys.value.push(nextKey++)
  // The places in the list moved: the messages of the last save no longer fit.
  save.reset()

  await nextTick()
  formEl.value?.querySelector<HTMLElement>('ul > li:last-child [data-series-label]')?.focus()
}

async function removeSeries(index: number): Promise<void> {
  if (!form.value) {
    return
  }

  form.value.Series.splice(index, 1)
  rowKeys.value.splice(index, 1)
  save.reset()

  // The button under the pointer is gone: the focus moves to the one now in its place, or to Add.
  await nextTick()

  const buttons = formEl.value?.querySelectorAll<HTMLElement>('[data-remove-series]') ?? []
  const next = buttons[Math.min(index, buttons.length - 1)] ?? formEl.value?.querySelector<HTMLElement>('[data-add-series]')

  next?.focus()
}
</script>

<template>
  <Sheet :open="open" @update:open="setOpen">
    <!-- A click outside does not close the sheet: it would throw away what was typed. -->
    <SheetContent
      class="w-full gap-0 data-[side=right]:w-full data-[side=right]:sm:max-w-2xl"
      @interact-outside="(event: Event) => event.preventDefault()"
    >
      <form ref="formEl" class="flex min-h-0 flex-1 flex-col" novalidate @submit.prevent="onSubmit">
        <SheetHeader class="border-b pr-12">
          <SheetTitle class="wrap-anywhere">{{ report ? `Edit report '${report.Title}'` : 'New report' }}</SheetTitle>
          <SheetDescription>A table or a chart of one or more series. Each series is a query of its own.</SheetDescription>
        </SheetHeader>

        <div class="grid flex-1 content-start gap-4 overflow-y-auto p-4">
          <LoadingState v-if="entities.loading.value" label="Loading entities" />

          <ErrorState v-else-if="entities.error.value" :error="entities.error.value" @retry="entities.reload" />

          <template v-else-if="form">
            <Alert v-if="errors.message" variant="destructive" role="alert">
              <CircleAlertIcon />
              <AlertDescription class="wrap-anywhere">{{ errors.message }}</AlertDescription>
            </Alert>

            <FormField v-slot="{ field }" label="Title" :error="errors.fields.Title">
              <Input v-model="form.Title" v-bind="field" placeholder="Report title" autocomplete="off" :disabled="save.pending.value" />
            </FormField>

            <div class="grid gap-4 sm:grid-cols-3">
              <FormField v-slot="{ field }" label="Visualization" :error="errors.fields.Type">
                <Select v-model="form.Type" :disabled="save.pending.value">
                  <SelectTrigger v-bind="field" class="w-full">
                    <SelectValue placeholder="Select a type" />
                  </SelectTrigger>
                  <SelectContent>
                    <SelectItem v-for="type in reportTypes" :key="type.value" :value="type.value">{{ type.label }}</SelectItem>
                  </SelectContent>
                </Select>
              </FormField>

              <FormField
                v-slot="{ field }"
                label="Time range"
                help="For series grouped by a date first."
                :error="errors.fields.TimeRange"
              >
                <TimeRangePicker :key="opening" v-model="form.TimeRange" v-bind="field" />
              </FormField>

              <FormField
                v-slot="{ field }"
                label="Max points per series"
                help="Top N: the most groups a series shows."
                :error="errors.fields.MaxRecords"
              >
                <Input
                  v-model="form.MaxRecords"
                  v-bind="field"
                  type="number"
                  min="1"
                  max="1000"
                  step="1"
                  inputmode="numeric"
                  :disabled="save.pending.value"
                />
              </FormField>
            </div>

            <section class="grid gap-3" :aria-labelledby="seriesHeadingId">
              <div>
                <h3 :id="seriesHeadingId" class="text-sm font-medium">Series</h3>
                <p class="mt-1 text-xs text-muted-foreground">
                  Each series is its own query and may read another entity. For the series to line up, group them by values
                  that can be compared, for example all by the same parts of a date. A row without a label, group-by and
                  property is not saved.
                </p>
              </div>

              <p v-if="errors.fields.Series" role="alert" class="text-xs text-destructive">{{ errors.fields.Series }}</p>

              <ul class="grid gap-3">
                <ReportSeriesEditor
                  v-for="(series, index) in form.Series"
                  :key="rowKeys[index]"
                  :model-value="series"
                  :app-token="appToken"
                  :type="form.Type"
                  :entities="entityList"
                  :index="index"
                  :errors="errors.series[index]"
                  :disabled="save.pending.value"
                  @update:model-value="(value) => setSeries(index, value)"
                  @remove="removeSeries(index)"
                />
              </ul>

              <div>
                <Button type="button" variant="outline" size="sm" :disabled="save.pending.value" data-add-series @click="addSeries">
                  <PlusIcon />
                  Add series
                </Button>
              </div>
            </section>
          </template>
        </div>

        <SheetFooter class="flex-row justify-end border-t">
          <Button type="button" variant="outline" :disabled="save.pending.value" @click="setOpen(false)">Cancel</Button>
          <Button type="submit" :disabled="save.pending.value || !form || entities.loading.value">
            <Loader2Icon v-if="save.pending.value" class="animate-spin" />
            {{ report ? 'Save' : 'Create' }}
          </Button>
        </SheetFooter>
      </form>
    </SheetContent>
  </Sheet>
</template>
