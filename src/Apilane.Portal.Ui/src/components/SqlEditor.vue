<script setup lang="ts">
import { CircleAlertIcon } from '@lucide/vue'
import { computed, onBeforeUnmount, onMounted, shallowRef, useAttrs, useTemplateRef, watch } from 'vue'
import { Skeleton } from '@/components/ui/skeleton'
import { Textarea } from '@/components/ui/textarea'
import type { SqlEditorHandle, SqlSchema } from '@/lib/sqlEditor'

// A SQL editor (CodeMirror 6): line numbers, SQL colours, brackets, and the names of the
// application's entities and properties offered while typing (Ctrl-Space opens the list; after
// 'Entity.' it lists that entity's properties). Use it wherever SQL is edited. CodeMirror is
// downloaded the first time an editor is shown; until then a placeholder of the same height shows.
// Inside a FormField bind the slot's `field` onto it like onto an Input:
//
//   <FormField v-slot="{ field }" label="SQL" :error="errors.fields.Query">
//     <SqlEditor v-model="form.Query" v-bind="field" label="SQL" :schema="schema" :database-type="app.DatabaseType" />
//   </FormField>
defineOptions({ inheritAttrs: false })

const props = withDefaults(
  defineProps<{
    /** The editor's name for screen readers (a <label> cannot name it). */
    label: string
    /** Entity name to its property names. It may arrive after the editor is shown. */
    schema?: SqlSchema
    /** The DatabaseType of the application ('SQLServer', 'MySQL', ...), for the keywords of its SQL. */
    databaseType?: string
    placeholder?: string
  }>(),
  { schema: () => ({}), databaseType: undefined, placeholder: undefined },
)

const text = defineModel<string>({ required: true })

const attrs = useAttrs()
const host = useTemplateRef<HTMLElement>('host')
const editor = shallowRef<SqlEditorHandle>()
// If CodeMirror cannot be downloaded (a deploy while the tab was open, a lost connection) the
// SQL is edited in a plain text box instead, so the screen still works.
const failed = shallowRef(false)

const invalid = computed(() => attrs['aria-invalid'] === true || attrs['aria-invalid'] === 'true')

// What FormField hands over (id, aria-invalid, aria-describedby) belongs on the editable element.
const contentAttributes = computed(() => {
  const result: Record<string, string> = { 'aria-label': props.label }

  for (const name of ['id', 'aria-invalid', 'aria-describedby']) {
    const value = attrs[name]

    if (value !== undefined && value !== null && value !== false) {
      result[name] = String(value)
    }
  }

  return result
})

let unmounted = false

onMounted(async () => {
  try {
    const { createSqlEditor } = await import('@/lib/sqlEditor')

    if (unmounted || !host.value) {
      return
    }

    editor.value = createSqlEditor(host.value, {
      text: text.value,
      schema: props.schema,
      databaseType: props.databaseType,
      placeholder: props.placeholder,
      attributes: contentAttributes.value,
      onChange: (value) => {
        text.value = value
      },
    })
  } catch {
    failed.value = true
  }
})

onBeforeUnmount(() => {
  unmounted = true
  editor.value?.destroy()
})

// A change from outside (Discard) replaces the editor's text; the editor's own changes come back equal.
watch(text, (value) => editor.value?.setText(value))
watch(
  () => props.schema,
  (schema) => editor.value?.setSchema(schema),
)
watch(contentAttributes, (value) => editor.value?.setAttributes(value))
</script>

<template>
  <div v-if="failed" class="grid gap-1.5">
    <Textarea
      v-model="text"
      v-bind="contentAttributes"
      :placeholder="placeholder"
      rows="10"
      spellcheck="false"
      class="font-mono text-xs md:text-xs"
    />
    <p class="flex items-center gap-1.5 text-xs text-muted-foreground">
      <CircleAlertIcon class="size-3.5 shrink-0" aria-hidden="true" />
      The SQL editor could not be loaded. Reload the page to get it back.
    </p>
  </div>

  <div
    v-else
    class="relative min-h-48 min-w-0 overflow-hidden rounded-lg border bg-input/30 transition-colors focus-within:border-ring focus-within:ring-3 focus-within:ring-ring/50"
    :class="invalid ? 'border-destructive/50 ring-3 ring-destructive/40' : 'border-input'"
  >
    <div ref="host" />
    <div v-if="!editor" role="status" class="absolute inset-0 p-2">
      <span class="sr-only">Loading the SQL editor</span>
      <Skeleton class="h-full w-full" />
    </div>
  </div>
</template>
