<script setup lang="ts">
import { watch } from 'vue'
import { useApplications } from '@/composables/useApplications'
import { useMutation } from '@/composables/useMutation'
import { api, unwrap } from '@/lib/api'
import { deleteConsequences, understandText } from '@/lib/applicationActions'
import type { Application } from '@/lib/applications'
import * as toast from '@/lib/toast'
import ConfirmByNameDialog from './ConfirmByNameDialog.vue'
import ConsequenceList from './ConsequenceList.vue'

// Asks (the consequences, an 'I understand' box and the name typed out), then deletes an
// application. The settings screen and the cards of the applications page both use it. After the
// delete it shows the toast and reloads the applications list, so the application leaves the page
// and the sidebar switcher; `deleted` is for leaving a screen of that application.
//
//   <DeleteApplicationDialog v-model:open="deleteOpen" :application="application" @deleted="..." />
const props = defineProps<{ application: Application }>()
const emit = defineEmits<{ deleted: [] }>()

const open = defineModel<boolean>('open', { required: true })

const { reload: reloadApplications } = useApplications()

const remove = useMutation(() =>
  unwrap(api.DELETE('/api/v1/applications/{appToken}', { params: { path: { appToken: props.application.Token } } })),
)

watch(open, (isOpen) => {
  if (isOpen) {
    remove.reset()
  }
})

async function submit(): Promise<boolean> {
  if (!(await remove.run())) {
    return false
  }

  toast.success(`Application ${props.application.Name} deleted.`)
  void reloadApplications()
  emit('deleted')
  return true
}
</script>

<template>
  <ConfirmByNameDialog
    v-model:open="open"
    :title="`Delete ${application.Name}?`"
    description="Deleting removes the application from the Portal and from its API server."
    :name="application.Name"
    :acknowledge="understandText"
    :action="submit"
    :error="remove.error.value?.message"
  >
    <ConsequenceList :items="deleteConsequences" />
  </ConfirmByNameDialog>
</template>
