<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import FileInput from '@/components/FileInput.vue'
import FormDialog from '@/components/FormDialog.vue'
import FormField from '@/components/FormField.vue'
import { useMutation } from '@/composables/useMutation'
import { apiServer } from '@/lib/apiServer'
import { formatCount } from '@/lib/entities'
import { fileTooLarge } from '@/lib/records'
import type { DataApplication } from '@/lib/records'

// Uploads one file to the application's Files (multipart POST {ServerUrl}/api/Files/Post, field
// FileUpload). A file larger than the application allows is refused before it is sent; the API
// server checks again on its own.
const props = defineProps<{ application: DataApplication }>()

const emit = defineEmits<{ uploaded: [] }>()

const open = defineModel<boolean>('open', { required: true })

const file = ref<File>()
const missing = ref(false)

const upload = useMutation((chosen: File) =>
  apiServer(props.application.Server.ServerUrl, props.application.Token).postFile<number>('/api/Files/Post', 'FileUpload', chosen),
)

const maxSize = computed(() => `${formatCount(props.application.MaxAllowedFileSizeInKB)} KB`)

const fileError = computed(() => {
  if (missing.value && !file.value) {
    return 'Choose a file to upload.'
  }

  if (file.value && fileTooLarge(file.value.size, props.application.MaxAllowedFileSizeInKB)) {
    return `The file is larger than the maximum of ${maxSize.value}.`
  }

  return upload.error.value?.message
})

watch(open, (isOpen) => {
  if (isOpen) {
    file.value = undefined
    missing.value = false
    upload.reset()
  }
})

watch(file, () => upload.reset())

async function submit(): Promise<boolean> {
  const chosen = file.value

  if (!chosen) {
    missing.value = true
    return false
  }

  if (fileTooLarge(chosen.size, props.application.MaxAllowedFileSizeInKB) || !(await upload.run(chosen))) {
    return false
  }

  emit('uploaded')
  return true
}
</script>

<template>
  <FormDialog v-model:open="open" title="Upload file" submit-label="Upload" :submit="submit">
    <FormField v-slot="{ field }" label="File" :help="`At most ${maxSize}.`" :error="fileError">
      <FileInput v-model="file" v-bind="field" />
    </FormField>
  </FormDialog>
</template>
