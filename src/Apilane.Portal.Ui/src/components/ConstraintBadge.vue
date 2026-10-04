<script setup lang="ts">
import { CheckCheckIcon, LinkIcon } from '@lucide/vue'
import { computed } from 'vue'
import { Badge } from '@/components/ui/badge'
import { constraintDescription, constraintLabel } from '@/lib/entities'
import type { Constraint } from '@/lib/entities'

// One unique or foreign key constraint of an entity as a badge. The badge shows the short text;
// the tooltip, and what a screen reader reads, is the full one ('Foreign key: ..., on delete cascade').
const props = defineProps<{ constraint: Constraint }>()

const foreignKey = computed(() => props.constraint.Type === 'ForeignKey')
</script>

<template>
  <Badge
    variant="outline"
    class="h-auto max-w-full shrink justify-start whitespace-normal"
    :class="foreignKey ? 'border-warning/40 text-warning' : 'border-link/40 text-link'"
    :title="constraintDescription(constraint)"
  >
    <component :is="foreignKey ? LinkIcon : CheckCheckIcon" aria-hidden="true" class="shrink-0" />
    <span aria-hidden="true" class="wrap-anywhere">{{ constraintLabel(constraint) }}</span>
    <span class="sr-only">{{ constraintDescription(constraint) }}</span>
  </Badge>
</template>
