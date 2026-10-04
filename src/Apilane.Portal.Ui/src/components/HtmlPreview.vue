<script setup lang="ts">
import { refDebounced } from '@vueuse/core'
import { toRef } from 'vue'

// Shows HTML that someone typed (an e-mail body) as a page, for a preview. Use it for any HTML that
// is not the Portal's own: it is drawn in a sandboxed frame with every permission off, so a script
// in it never runs, and a form or link in it cannot leave the frame.
//
//   <HtmlPreview :html="form.Content" title="Preview of the email body" />
const props = defineProps<{
  html: string
  /** Names the frame for screen readers. */
  title: string
}>()

// Every change reloads the frame: wait until the typing pauses.
const shown = refDebounced(toRef(props, 'html'), 300)
</script>

<template>
  <!-- sandbox is set before the frame is added to the page, so it never runs unsandboxed. -->
  <iframe sandbox="" :srcdoc="shown" :title="title" class="h-72 w-full rounded-lg border bg-preview" />
</template>
