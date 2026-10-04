<script setup lang="ts">
import { watch } from 'vue'
import { useMutation } from '@/composables/useMutation'
import { api, unwrap } from '@/lib/api'
import { rebuildConsequences, understandText } from '@/lib/applicationActions'
import type { Application } from '@/lib/applications'
import * as toast from '@/lib/toast'
import ConfirmByNameDialog from './ConfirmByNameDialog.vue'
import ConsequenceList from './ConsequenceList.vue'

// Asks (the consequences, an 'I understand' box and the name typed out), then rebuilds an
// application: all its data is dropped, its token, entities and properties stay. The settings
// screen and the cards of the applications page both use it. `done` fires after a rebuild.
//
//   <RebuildApplicationDialog v-model:open="rebuildOpen" :application="application" @done="..." />
const props = defineProps<{ application: Application }>()

const open = defineModel<boolean>('open', { required: true })
const emit = defineEmits<{ done: [] }>()

const rebuild = useMutation(() =>
  unwrap(api.POST('/api/v1/applications/{appToken}/rebuild', { params: { path: { appToken: props.application.Token } } })),
)

watch(open, (isOpen) => {
  if (isOpen) {
    rebuild.reset()
  }
})

async function submit(): Promise<boolean> {
  if (!(await rebuild.run())) {
    return false
  }

  toast.success(`${props.application.Name} rebuilt. All its data was removed.`)
  emit('done')
  return true
}
</script>

<template>
  <ConfirmByNameDialog
    v-model:open="open"
    :title="`Rebuild ${application.Name}?`"
    description="Rebuilding drops the application's tables and creates them again, empty."
    :name="application.Name"
    confirm-label="Rebuild"
    :opener-removed="false"
    :acknowledge="understandText"
    :action="submit"
    :error="rebuild.error.value?.message"
  >
    <ConsequenceList :items="rebuildConsequences" />
  </ConfirmByNameDialog>
</template>
