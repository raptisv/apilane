<script setup lang="ts">
import { CircleAlertIcon, Loader2Icon, SendIcon } from '@lucide/vue'
import { computed, reactive, shallowRef, watch } from 'vue'
import CopyField from '@/components/CopyField.vue'
import FormField from '@/components/FormField.vue'
import { Alert, AlertDescription } from '@/components/ui/alert'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { useMutation } from '@/composables/useMutation'
import { apiServer } from '@/lib/apiServer'
import { endpointAddress, testParameters, testQueryBody } from '@/lib/customEndpoints'

// The right-hand panel of the custom endpoint editor: the address the endpoint will have, a box per
// parameter of the SQL, and Test, which runs the SQL as it is in the editor (saved or not) on the
// API server and shows what it returns, or the database's error.
const props = defineProps<{
  serverUrl: string
  appToken: string
  name: string
  query: string
}>()

// Computed here as the Portal computes it, so it follows every key press without a request.
const address = computed(() => endpointAddress(props.serverUrl, props.appToken, props.name, props.query))

// A value stays when its parameter leaves the SQL and comes back, so editing the SQL loses nothing typed.
// The boxes are text boxes: a number box would hand over a JavaScript number, which rounds a long
// integer above 2^53 (a 64-bit ID). The text is sent as typed.
const values = reactive<Record<string, string | number>>({})

const result = shallowRef<unknown>()

const test = useMutation(async () => {
  result.value = await apiServer(props.serverUrl, props.appToken).post<unknown>(
    '/api/Custom/TestQuery',
    testQueryBody(props.query),
    testParameters(address.value.Parameters, values),
  )
})

async function run(): Promise<void> {
  if (!(await test.run())) {
    // An error replaces the last result.
    result.value = undefined
  }
}

// The error was about the SQL as it was: a change to the name or the SQL removes it.
watch(
  () => [props.name, props.query],
  () => test.reset(),
)

const resultText = computed(() => (result.value === undefined ? '' : JSON.stringify(result.value, null, 4)))
</script>

<template>
  <section class="grid content-start gap-4 rounded-lg border bg-card p-4" aria-labelledby="test-title">
    <h2 id="test-title" class="text-sm font-semibold">Result</h2>

    <CopyField label="URL" :value="address.Url" />

    <fieldset v-if="address.Parameters.length > 0" class="grid gap-3">
      <legend class="mb-2 text-sm font-medium">Parameters</legend>
      <FormField v-for="parameter in address.Parameters" :key="parameter" v-slot="{ field }" :label="parameter">
        <Input v-model="values[parameter]" v-bind="field" type="text" inputmode="numeric" autocomplete="off" :placeholder="parameter" />
      </FormField>
      <p class="text-xs text-muted-foreground">Whole numbers only. A box left empty is sent as SQL null.</p>
    </fieldset>

    <div>
      <Button type="button" :disabled="test.pending.value" @click="run">
        <Loader2Icon v-if="test.pending.value" class="animate-spin" />
        <SendIcon v-else />
        Test
      </Button>
    </div>

    <Alert v-if="test.error.value" variant="destructive" role="alert">
      <CircleAlertIcon />
      <AlertDescription class="wrap-anywhere">{{ test.error.value.message }}</AlertDescription>
    </Alert>

    <div v-if="result !== undefined" class="grid gap-1.5">
      <h3 class="text-sm font-medium">What the query returned</h3>
      <pre
        class="max-h-96 overflow-auto rounded-lg border bg-background p-3 font-mono text-xs whitespace-pre"
        tabindex="0"
        aria-label="What the query returned"
      >{{ resultText }}</pre>
    </div>

    <p v-else-if="!test.error.value" class="text-sm text-muted-foreground">
      Test runs the SQL as it is in the editor, with the values above, and shows what it returns here.
    </p>
  </section>
</template>
