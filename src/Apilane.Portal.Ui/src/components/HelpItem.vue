<script setup lang="ts">
import { ChevronRightIcon } from '@lucide/vue'

// One expandable item of a HelpSheet. Its text is plain paragraphs, <ul> and <ol> lists, <strong> for the
// name of a control and <code> for a name the person types.
defineProps<{
  title: string
  /** Open when the help is shown: only for the first item, "What you can do here". */
  open?: boolean
}>()
</script>

<template>
  <details class="group min-w-0" :open="open">
    <summary
      class="flex cursor-pointer list-none items-center gap-2 px-4 py-2.5 text-sm font-medium select-none hover:bg-muted/40 focus-visible:outline-2 focus-visible:-outline-offset-2 focus-visible:outline-ring [&::-webkit-details-marker]:hidden"
    >
      <ChevronRightIcon class="size-4 shrink-0 text-muted-foreground transition-transform group-open:rotate-90" aria-hidden="true" />
      {{ title }}
    </summary>
    <div
      class="grid gap-2 px-4 pb-3.5 pl-10 text-sm leading-relaxed text-muted-foreground [&_code]:rounded-sm [&_code]:bg-muted [&_code]:px-1 [&_code]:font-mono [&_code]:text-xs [&_code]:text-foreground [&_li]:pl-0.5 [&_ol]:grid [&_ol]:list-decimal [&_ol]:gap-1.5 [&_ol]:pl-5 [&_strong]:font-medium [&_strong]:text-foreground [&_ul]:grid [&_ul]:list-disc [&_ul]:gap-1.5 [&_ul]:pl-5"
    >
      <slot />
    </div>
  </details>
</template>
