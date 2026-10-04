<script setup lang="ts">
import { AppWindowIcon, ArrowRightIcon, CircleAlertIcon, CircleCheckIcon, GitCompareArrowsIcon, RefreshCwIcon } from '@lucide/vue'
import { computed } from 'vue'
import ApplicationSelect from '@/components/ApplicationSelect.vue'
import DiffSection from '@/components/diff/DiffSection.vue'
import FieldChange from '@/components/diff/FieldChange.vue'
import FullScreenDialog from '@/components/FullScreenDialog.vue'
import LoadingState from '@/components/LoadingState.vue'
import StateMessage from '@/components/StateMessage.vue'
import { Alert, AlertDescription, AlertTitle } from '@/components/ui/alert'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Label } from '@/components/ui/label'
import { useAsync } from '@/composables/useAsync'
import { api, unwrap } from '@/lib/api'
import type { Application } from '@/lib/applications'
import { comparisonView, constraintTypeName, isIdentical, ruleText } from '@/lib/comparison'
import ComparedProperty from './ComparedProperty.vue'

// What differs between one application (`source`, the card that was clicked) and another one of
// the user's applications, picked here (`target`, its token). Added is what only the other one has,
// Removed what only the source has. Nothing is changed by comparing.
//
//   <CompareApplicationsDialog v-model:open="open" v-model:target="otherToken" :source="application" :applications="applications" />
const props = defineProps<{
  source: Application
  /** The user's applications; the source itself is left out of the picker. */
  applications: readonly Application[]
}>()

const open = defineModel<boolean>('open', { required: true })
/** The token of the application compared with; undefined until one is picked. */
const target = defineModel<string | undefined>('target', { required: true })

const others = computed(() => props.applications.filter((application) => application.Token !== props.source.Token))
// Only one of the user's other applications counts: anything else in the address is 'none picked yet'.
const targetToken = computed(() => others.value.find((application) => application.Token === target.value)?.Token)

const { data, error, loading, reload } = useAsync(
  async () => {
    const other = targetToken.value

    if (!other) {
      return undefined
    }

    const comparison = await unwrap(
      api.GET('/api/v1/applications/{appToken}/comparison', {
        params: { path: { appToken: props.source.Token }, query: { Target: other } },
      }),
    )

    return { source: comparison.ApplicationSource, target: comparison.ApplicationTarget, view: comparisonView(comparison) }
  },
  { watch: [() => props.source.Token, targetToken] },
)

const sqlClass = 'mt-1 rounded-md bg-muted/50 p-2 font-mono text-xs wrap-anywhere whitespace-pre-wrap'
const codeClass = 'font-mono text-xs wrap-anywhere'
</script>

<template>
  <FullScreenDialog
    v-model:open="open"
    title="Compare applications"
    :description="`What differs in entities, properties, constraints, custom endpoints and security rules. Added is what only the other application has, Removed what only ${source.Name} has. Reports, settings and data are not compared.`"
  >
    <template #header>
      <div v-if="others.length > 0" class="mt-1 grid gap-1.5 sm:max-w-sm">
        <Label for="compare-with" class="wrap-anywhere">Compare {{ source.Name }} with</Label>
        <ApplicationSelect
          id="compare-with"
          :model-value="targetToken"
          :applications="others"
          @update:model-value="(token) => (target = token)"
        />
      </div>
    </template>

    <StateMessage
      v-if="others.length === 0"
      :icon="AppWindowIcon"
      title="No other application"
      :description="`${source.Name} is your only application, so there is nothing to compare it with.`"
    />

    <StateMessage
      v-else-if="!targetToken"
      :icon="GitCompareArrowsIcon"
      title="Select an application"
      :description="`Pick the application to compare ${source.Name} with.`"
    />

    <LoadingState v-else-if="loading" label="Comparing the applications" />

    <Alert v-else-if="error" variant="destructive">
      <CircleAlertIcon />
      <AlertTitle>Could not load the comparison</AlertTitle>
      <AlertDescription>
        <p>{{ error.message }}</p>
        <Button variant="outline" size="sm" class="mt-3" @click="reload">
          <RefreshCwIcon />
          Try again
        </Button>
      </AlertDescription>
    </Alert>

    <template v-else-if="data">
      <p class="mb-6 flex flex-wrap items-center gap-2 rounded-lg bg-muted/40 px-3 py-2 text-sm font-medium">
        <span class="wrap-anywhere">{{ data.source }}</span>
        <ArrowRightIcon class="size-4 shrink-0 text-muted-foreground" aria-hidden="true" />
        <span class="sr-only">compared with</span>
        <span class="wrap-anywhere">{{ data.target }}</span>
      </p>

      <StateMessage
        v-if="isIdentical(data.view)"
        :icon="CircleCheckIcon"
        title="Applications are identical"
        description="Their entities, custom endpoints and security rules are the same."
      />

      <div v-else class="grid gap-8">
        <DiffSection
          title="Entities"
          :added="data.view.entities.added"
          :removed="data.view.entities.removed"
          :changed="data.view.entities.changed"
        >
          <template #item="{ item }">
            <div class="flex flex-wrap items-center gap-x-2 gap-y-1 text-sm">
              <span class="font-medium wrap-anywhere">{{ item.Name }}</span>
              <Badge v-if="item.RequireChangeTracking" variant="secondary">Change tracking</Badge>
              <Badge v-if="item.HasDifferentiationProperty" variant="secondary">Differentiation</Badge>
            </div>
            <p v-if="item.Description" class="mt-0.5 text-xs wrap-anywhere text-muted-foreground">{{ item.Description }}</p>

            <p v-if="item.Properties.length + item.Constraints.length === 0" class="mt-1 text-xs text-muted-foreground">
              No custom properties or constraints.
            </p>
            <ul v-else class="mt-2 grid gap-1.5 pl-3">
              <li v-for="property in item.Properties" :key="property.Name"><ComparedProperty :property="property" /></li>
              <li v-for="(constraint, index) in item.Constraints" :key="index" class="flex flex-wrap items-center gap-2 text-sm">
                <Badge variant="outline" class="border-warning/40 text-warning">{{ constraintTypeName(constraint.TypeID) }}</Badge>
                <span :class="codeClass">{{ constraint.Properties }}</span>
              </li>
            </ul>
          </template>

          <template #changed="{ item }">
            <p class="text-sm font-medium wrap-anywhere">{{ item.name }}</p>
            <FieldChange
              v-for="change in item.changes"
              :key="change.field"
              class="mt-1"
              :field="change.field"
              :before="change.before"
              :after="change.after"
            />
          </template>
        </DiffSection>

        <DiffSection
          title="Properties"
          :added="data.view.properties.added"
          :removed="data.view.properties.removed"
          :changed="data.view.properties.changed"
        >
          <template #item="{ item }">
            <ComparedProperty :property="item.item" :entity="item.entity" />
          </template>

          <template #changed="{ item }">
            <p class="text-sm font-medium wrap-anywhere">{{ item.name }}</p>
            <FieldChange
              v-for="change in item.changes"
              :key="change.field"
              class="mt-1"
              :field="change.field"
              :before="change.before"
              :after="change.after"
            />
          </template>
        </DiffSection>

        <DiffSection title="Constraints" :added="data.view.constraints.added" :removed="data.view.constraints.removed">
          <template #item="{ item }">
            <div class="flex flex-wrap items-center gap-2 text-sm">
              <span class="font-medium wrap-anywhere">{{ item.entity }}</span>
              <Badge variant="outline" class="border-warning/40 text-warning">{{ constraintTypeName(item.item.TypeID) }}</Badge>
              <span :class="codeClass">{{ item.item.Properties }}</span>
            </div>
          </template>
        </DiffSection>

        <DiffSection
          title="Custom endpoints"
          :added="data.view.customEndpoints.added"
          :removed="data.view.customEndpoints.removed"
          :changed="data.view.customEndpoints.changed"
        >
          <template #item="{ item }">
            <p class="text-sm font-medium wrap-anywhere">{{ item.Name }}</p>
            <p v-if="item.Description" class="mt-0.5 text-xs wrap-anywhere text-muted-foreground">{{ item.Description }}</p>
            <pre :class="sqlClass">{{ item.Query }}</pre>
          </template>

          <template #changed="{ item }">
            <p class="text-sm font-medium wrap-anywhere">{{ item.name }}</p>
            <FieldChange
              v-for="change in item.changes"
              :key="change.field"
              class="mt-1"
              :field="change.field"
              :before="change.before"
              :after="change.after"
              :code="change.code"
            />
          </template>
        </DiffSection>

        <DiffSection
          title="Security"
          :added="data.view.security.added"
          :removed="data.view.security.removed"
          :changed="data.view.security.changed"
        >
          <template #item="{ item }">
            <p class="text-sm wrap-anywhere">
              <span class="font-medium">{{ item.Name }}</span>
              <span class="ml-2 text-xs text-muted-foreground">{{ ruleText(item) }}</span>
            </p>
          </template>

          <template #changed="{ item }">
            <p class="text-sm font-medium wrap-anywhere">{{ item.name }}</p>
            <FieldChange
              v-for="change in item.changes"
              :key="change.field"
              class="mt-1"
              :field="change.field"
              :before="change.before"
              :after="change.after"
            />
          </template>
        </DiffSection>
      </div>
    </template>
  </FullScreenDialog>
</template>
