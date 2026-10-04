<script setup lang="ts">
// One value that differs between two sides of a comparison: the field, its value before and its
// value after. A value that is not set reads 'not set' and an empty text reads 'empty', so a change
// from one to the other shows. `code` is for a long value such as SQL: the two values are shown as
// blocks in a fixed-width font that wrap instead of scrolling sideways.
//
//   <FieldChange field="Maximum" before="10" after="20" />
//   <FieldChange field="Query" :before="old.Query" :after="new.Query" code />
defineProps<{
  field: string
  before?: string | null
  after?: string | null
  code?: boolean
}>()

const blockClass = 'mt-1 rounded-md bg-muted/50 p-2 font-mono text-xs wrap-anywhere whitespace-pre-wrap'
</script>

<template>
  <div v-if="code" class="text-sm">
    <p class="font-medium">{{ field }}</p>
    <div class="mt-1 grid gap-2 lg:grid-cols-2">
      <div class="min-w-0">
        <p class="text-xs text-destructive">Before</p>
        <pre v-if="before" :class="blockClass">{{ before }}</pre>
        <p v-else class="mt-1 text-xs text-muted-foreground italic">{{ before == null ? 'not set' : 'empty' }}</p>
      </div>
      <div class="min-w-0">
        <p class="text-xs text-success">After</p>
        <pre v-if="after" :class="blockClass">{{ after }}</pre>
        <p v-else class="mt-1 text-xs text-muted-foreground italic">{{ after == null ? 'not set' : 'empty' }}</p>
      </div>
    </div>
  </div>

  <p v-else class="text-sm wrap-anywhere">
    <span class="font-medium">{{ field }}</span
    >:
    <span v-if="before" class="text-destructive">{{ before }}</span>
    <span v-else class="text-muted-foreground italic">{{ before == null ? 'not set' : 'empty' }}</span>
    <span aria-hidden="true"> → </span>
    <span class="sr-only"> changed to </span>
    <span v-if="after" class="text-success">{{ after }}</span>
    <span v-else class="text-muted-foreground italic">{{ after == null ? 'not set' : 'empty' }}</span>
  </p>
</template>
