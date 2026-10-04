<script setup lang="ts">
import { Badge } from '@/components/ui/badge'

// A count of a comparison as a badge: '2 added', '3 new entities'. DiffSection shows them next to
// its title; use it on its own for a summary line (what a loaded schema diff holds).
//
//   <DiffCount tone="added" :count="2" label="new entities" />
defineProps<{
  count: number
  label: string
  tone: 'added' | 'removed' | 'changed'
}>()

const toneClass = {
  added: 'border-success/40 text-success',
  removed: 'border-destructive/40 text-destructive',
  changed: 'border-warning/40 text-warning',
}
</script>

<template>
  <Badge variant="outline" :class="toneClass[tone]">{{ count }} {{ label }}</Badge>
</template>
