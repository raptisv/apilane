<script setup lang="ts">
import { Loader2Icon } from '@lucide/vue'
import ConfirmDialog from '@/components/ConfirmDialog.vue'
import { Button } from '@/components/ui/button'
import { useLeaveGuard } from '@/composables/useLeaveGuard'

// The bar of a screen that collects several edits before one Save (constraints, default sorting):
// it appears at the bottom of the window while there are unsaved changes, with Discard and Save.
// It also guards the way out: leaving with unsaved changes asks first (useLeaveGuard).
// Keep it mounted for the whole life of the screen (no v-if on it), or the guard is gone.
//
//   <UnsavedChangesBar :dirty="dirty" :pending="save.pending.value" @save="submit" @discard="discard" />
const props = withDefaults(
  defineProps<{
    /** True while the screen holds changes that are not saved. */
    dirty: boolean
    /** True while the save is running. */
    pending?: boolean
    /** What is waiting, for example '2 unsaved changes'. */
    message?: string
  }>(),
  { message: 'You have unsaved changes.' },
)

defineEmits<{ save: []; discard: [] }>()

const { open: leaveOpen, leave } = useLeaveGuard(() => props.dirty)
</script>

<template>
  <div
    v-if="dirty"
    role="region"
    aria-label="Unsaved changes"
    class="sticky bottom-4 z-20 mt-6 flex flex-wrap items-center justify-between gap-x-4 gap-y-2 rounded-lg border bg-popover px-4 py-3 shadow-lg"
  >
    <p class="text-sm" role="status">{{ message }}</p>
    <div class="flex items-center gap-2">
      <Button variant="outline" :disabled="pending" @click="$emit('discard')">Discard</Button>
      <Button :disabled="pending" @click="$emit('save')">
        <Loader2Icon v-if="pending" class="animate-spin" />
        Save
      </Button>
    </div>
  </div>

  <ConfirmDialog
    v-model:open="leaveOpen"
    title="Leave without saving?"
    description="Your changes on this screen are not saved. If you leave, they are lost."
    confirm-label="Leave"
    :action="leave"
  />
</template>
