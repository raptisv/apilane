<script setup lang="ts">
import { ref, watch } from 'vue'
import { useApplications } from '@/composables/useApplications'
import { useMutation } from '@/composables/useMutation'
import { api, unwrap } from '@/lib/api'
import { statusChange } from '@/lib/applicationActions'
import type { Application } from '@/lib/applications'
import * as toast from '@/lib/toast'
import ConfirmDialog from './ConfirmDialog.vue'

// Asks, then takes an application offline or brings it online: the opposite of its status now.
// The settings screen and the cards of the applications page both use it. After the change it
// shows the toast and reloads the applications list; `done` is for anything else the screen shows
// (the settings screen reloads the application).
//
//   <ApplicationStatusDialog v-model:open="statusOpen" :application="application" @done="reload" />
const props = defineProps<{ application: Application }>()
const emit = defineEmits<{ done: [] }>()

const open = defineModel<boolean>('open', { required: true })

const { reload: reloadApplications } = useApplications()

// Fixed when the dialog opens, so its texts do not flip while it fades out after the change.
const change = ref(statusChange(props.application.Name, props.application.Online))

const setStatus = useMutation(() =>
  unwrap(
    api.PUT('/api/v1/applications/{appToken}/status', {
      params: { path: { appToken: props.application.Token } },
      body: { Online: change.value.Online },
    }),
  ),
)

watch(open, (isOpen) => {
  if (isOpen) {
    change.value = statusChange(props.application.Name, props.application.Online)
    setStatus.reset()
  }
})

async function submit(): Promise<boolean> {
  if (!(await setStatus.run())) {
    return false
  }

  toast.success(change.value.done)
  void reloadApplications()
  emit('done')
  return true
}
</script>

<template>
  <ConfirmDialog
    v-model:open="open"
    :title="change.title"
    :description="change.description"
    :confirm-label="change.label"
    :destructive="change.destructive"
    :action="submit"
    :error="setStatus.error.value?.message"
  />
</template>
