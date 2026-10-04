<script setup lang="ts">
// A file picker for one file: the browser's own control, styled like Input. It shows the name of
// the chosen file itself. Use it inside FormField and send the file with toFormData (lib/formData.ts):
//
//   <FormField v-slot="{ field }" label="File" :error="errors.fields.File">
//     <FileInput v-model="file" v-bind="field" accept=".json" />
//   </FormField>
defineProps<{
  /** The file types the picker offers, e.g. '.json'. The API still checks what arrives. */
  accept?: string
}>()

const file = defineModel<File | undefined>({ required: true })

// Also on 'cancel': Chromium fires 'cancel', not 'change', when the same file is picked again, and
// its File is a new one that reads the current content of the file.
function onChange(event: Event): void {
  file.value = (event.target as HTMLInputElement).files?.[0]
}
</script>

<template>
  <input
    type="file"
    :accept="accept"
    class="h-8 w-full min-w-0 cursor-pointer rounded-lg border border-input bg-input/30 pr-2.5 text-sm text-muted-foreground outline-none transition-colors file:mr-3 file:h-full file:cursor-pointer file:border-0 file:border-r file:border-solid file:border-input file:bg-muted file:px-3 file:text-sm file:font-medium file:text-foreground focus-visible:border-ring focus-visible:ring-3 focus-visible:ring-ring/50 aria-invalid:border-destructive/50 aria-invalid:ring-3 aria-invalid:ring-destructive/40"
    @change="onChange"
    @cancel="onChange"
  />
</template>
