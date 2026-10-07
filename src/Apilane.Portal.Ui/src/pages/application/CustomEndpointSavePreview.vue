<script setup lang="ts">
import { TriangleAlertIcon } from '@lucide/vue'
import { computed } from 'vue'
import LineDiff from '@/components/diff/LineDiff.vue'
import { Button } from '@/components/ui/button'
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from '@/components/ui/dialog'

// Shown when an existing custom endpoint is saved: the name and description that change and a line by
// line comparison of the SQL, so the person sees what goes live before it does. Back keeps the form as
// it is; Save asks the screen to save.
const props = defineProps<{
  before: { Name: string; Description: string; Query: string }
  after: { Name: string; Description: string; Query: string }
  /** The rename drops the security rules written for the old name: say so here too. */
  renaming: boolean
}>()

const open = defineModel<boolean>('open', { required: true })
const emit = defineEmits<{ confirm: [] }>()

const nameChanged = computed(() => props.before.Name !== props.after.Name)
const descriptionChanged = computed(() => props.before.Description !== props.after.Description)

function confirm(): void {
  open.value = false
  emit('confirm')
}

const valueClass = 'rounded-sm bg-muted/60 px-1 font-mono wrap-anywhere'
</script>

<template>
  <Dialog v-model:open="open">
    <DialogContent class="flex max-h-[calc(100dvh-2rem)] flex-col sm:max-w-3xl">
      <DialogHeader class="pr-8">
        <DialogTitle>Review the changes</DialogTitle>
        <DialogDescription>
          This is what changes in {{ before.Name }}. Once saved, the API server runs the new SQL on the next call.
        </DialogDescription>
      </DialogHeader>

      <div class="-mx-4 grid min-h-0 flex-1 content-start gap-4 overflow-y-auto px-4">
        <dl v-if="nameChanged || descriptionChanged" class="grid gap-2 text-sm">
          <div v-if="nameChanged">
            <dt class="font-medium">Name</dt>
            <dd>
              <span :class="[valueClass, 'text-destructive line-through']">{{ before.Name }}</span>
              <span aria-hidden="true"> → </span>
              <span class="sr-only">changed to</span>
              <span :class="[valueClass, 'text-success']">{{ after.Name }}</span>
            </dd>
          </div>
          <div v-if="descriptionChanged">
            <dt class="font-medium">Description</dt>
            <dd class="grid gap-1">
              <span v-if="before.Description" :class="[valueClass, 'w-fit text-destructive line-through']">{{ before.Description }}</span>
              <span v-else class="text-xs text-muted-foreground italic">was empty</span>
              <span v-if="after.Description" :class="[valueClass, 'w-fit text-success']">{{ after.Description }}</span>
              <span v-else class="text-xs text-muted-foreground italic">now empty</span>
            </dd>
          </div>
        </dl>

        <div
          v-if="renaming"
          class="flex gap-2.5 rounded-lg border border-warning/40 bg-warning/10 p-3 text-sm text-warning"
          role="note"
        >
          <TriangleAlertIcon class="mt-0.5 size-4 shrink-0" aria-hidden="true" />
          <p>The address changes with the name, and the security rules written for {{ before.Name }} no longer apply to it.</p>
        </div>

        <div class="grid gap-1.5">
          <h3 class="text-sm font-medium">SQL</h3>
          <LineDiff :before="before.Query" :after="after.Query" />
        </div>
      </div>

      <DialogFooter>
        <Button variant="outline" @click="open = false">Back</Button>
        <Button @click="confirm">Save</Button>
      </DialogFooter>
    </DialogContent>
  </Dialog>
</template>
