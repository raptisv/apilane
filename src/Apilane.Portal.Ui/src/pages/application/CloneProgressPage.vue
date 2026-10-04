<script setup lang="ts">
import { ArrowLeftIcon, ArrowRightIcon, CircleCheckIcon, ClockIcon, CompassIcon, TriangleAlertIcon } from '@lucide/vue'
import { computed, watch } from 'vue'
import { useRoute } from 'vue-router'
import ErrorState from '@/components/ErrorState.vue'
import LoadingState from '@/components/LoadingState.vue'
import PageHeader from '@/components/PageHeader.vue'
import StateMessage from '@/components/StateMessage.vue'
import { Alert, AlertDescription, AlertTitle } from '@/components/ui/alert'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Progress } from '@/components/ui/progress'
import { useApplication } from '@/composables/useApplication'
import { useApplications } from '@/composables/useApplications'
import { usePolling } from '@/composables/usePolling'
import { api, ApiError, unwrap } from '@/lib/api'
import { cloneCounters, clonePercent, currentEntities, etaText, failureText, isFinished, phaseText } from '@/lib/clone'

// The progress of one clone, asked from the Portal every 2 seconds until it has completed or
// failed. The clone itself runs on the Portal: leaving this screen does not stop it.
const route = useRoute()
const { application } = useApplication()
const { reload: reloadApplications } = useApplications()

// AppLayout gives every application a fresh screen, so the token is fixed for the life of this one.
const appToken = application.value.Token

// The Portal keeps operations in memory. A 404 will not go away: the operation is unknown, was
// started by someone else, or was forgotten (30 minutes after it completed, or by a restart).
function isGone(error: Error | undefined): boolean {
  return error instanceof ApiError && error.status === 404
}

const {
  data: operation,
  error,
  loading,
  restart,
} = usePolling(
  () =>
    unwrap(
      api.GET('/api/v1/applications/{appToken}/clones/{operationId}', {
        params: { path: { appToken, operationId: String(route.params.operationId) } },
      }),
    ),
  { intervalMs: 2000, done: isFinished, giveUp: isGone },
)

// A jump in the browser history from one clone of this application to another keeps this
// screen: drop the old answer and ask for the new operation.
watch(
  () => route.params.operationId,
  () => {
    operation.value = undefined
    restart()
  },
)

const gone = computed(() => isGone(error.value))

// The new application shows in the sidebar switcher and on the applications page.
watch(
  () => operation.value?.Status,
  (status) => {
    if (status === 'Completed') {
      void reloadApplications()
    }
  },
)

const cloneFormRoute = { name: 'app-clone', params: { appToken } }
const rowClass = 'flex flex-wrap items-center justify-between gap-x-4 gap-y-1 rounded-md bg-muted/40 px-3 py-2 text-sm'
</script>

<template>
  <PageHeader title="Clone progress" :description="`The clone of ${application.Name}.`" class="wrap-anywhere" />

  <LoadingState v-if="loading" label="Loading the progress of the clone" />

  <StateMessage
    v-else-if="gone"
    :icon="CompassIcon"
    title="This clone can no longer be tracked"
    description="The Portal keeps the progress of a clone in memory: for 30 minutes after it completes, and not across a restart. The clone may have finished all the same: look for it in your applications."
  >
    <Button as-child>
      <RouterLink :to="{ name: 'apps' }">Go to applications</RouterLink>
    </Button>
    <Button as-child variant="outline">
      <RouterLink :to="cloneFormRoute">Back to clone</RouterLink>
    </Button>
  </StateMessage>

  <ErrorState v-else-if="error && !operation" :error="error" @retry="restart" />

  <div v-else-if="operation" class="grid max-w-2xl gap-4">
    <template v-if="operation.Status === 'Completed'">
      <Alert class="border-success/40 text-success">
        <CircleCheckIcon />
        <AlertTitle>Clone completed</AlertTitle>
        <AlertDescription class="text-success/90">The application has been cloned.</AlertDescription>
      </Alert>

      <div>
        <Button as-child>
          <RouterLink :to="{ name: 'app-entities', params: { appToken: operation.ClonedApplicationToken } }">
            <ArrowRightIcon />
            Go to cloned application
          </RouterLink>
        </Button>
      </div>
    </template>

    <template v-else-if="operation.Status === 'Failed'">
      <Alert variant="destructive">
        <TriangleAlertIcon />
        <AlertTitle>Clone failed</AlertTitle>
        <AlertDescription class="wrap-anywhere">{{ failureText(operation) }}</AlertDescription>
      </Alert>

      <p class="text-sm text-muted-foreground">
        The new application was added to your applications before the clone failed. Delete it there if you do not need
        it.
      </p>

      <div class="flex flex-wrap gap-2">
        <Button as-child variant="secondary">
          <RouterLink :to="cloneFormRoute">
            <ArrowLeftIcon />
            Back to clone
          </RouterLink>
        </Button>
        <Button as-child variant="outline">
          <RouterLink :to="{ name: 'apps' }">Go to applications</RouterLink>
        </Button>
      </div>
    </template>

    <section v-else class="grid gap-4 rounded-lg border bg-card p-4" aria-label="Progress">
      <div class="grid gap-2">
        <div class="flex items-center justify-between gap-3">
          <p class="text-sm font-medium" role="status">{{ phaseText(operation.Status) }}</p>
          <Badge variant="secondary" class="font-mono">{{ clonePercent(operation) }}%</Badge>
        </div>
        <Progress :model-value="clonePercent(operation)" class="h-2" aria-label="Clone progress" />
      </div>

      <dl class="grid gap-2 sm:grid-cols-2">
        <div v-for="counter in cloneCounters(operation)" :key="counter.label" :class="rowClass">
          <dt class="text-muted-foreground">{{ counter.label }}</dt>
          <dd class="font-mono">{{ counter.value }}</dd>
        </div>
      </dl>

      <p v-for="current in currentEntities(operation)" :key="current.label" :class="rowClass">
        <span class="min-w-0">
          {{ current.label }}:
          <strong class="font-mono font-medium wrap-anywhere">{{ current.name }}</strong>
        </span>
        <span v-if="current.records" class="font-mono text-muted-foreground">{{ current.records }}</span>
      </p>

      <p v-if="etaText(operation)" class="flex items-center gap-2 text-sm text-muted-foreground">
        <ClockIcon class="size-4 shrink-0" aria-hidden="true" />
        <span>
          Estimated time remaining:
          <strong class="font-medium text-foreground">{{ etaText(operation) }}</strong>
        </span>
      </p>

      <p v-if="error" class="text-xs text-warning" role="status">The Portal did not answer. Trying again…</p>

      <p class="text-xs text-muted-foreground">
        The clone runs on the Portal. You can leave this page and come back to this address to see how far it is. Stay
        signed in until it has finished: the clone works with your session.
      </p>
    </section>
  </div>
</template>
