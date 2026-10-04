<script setup lang="ts">
import { Badge } from '@/components/ui/badge'
import { propertyFacts } from '@/lib/comparison'
import type { ListedProperty } from '@/lib/comparison'

// One property in the compare dialog: its name, type, the values that are set and its description.
// `entity` goes in front of the name where the property is not listed under its entity.
defineProps<{ property: ListedProperty; entity?: string }>()
</script>

<template>
  <div class="text-sm">
    <div class="flex flex-wrap items-center gap-x-2 gap-y-1">
      <span class="font-medium wrap-anywhere">
        <span v-if="entity" class="text-muted-foreground">{{ entity }}.</span>{{ property.Name }}
      </span>
      <Badge variant="secondary">{{ property.TypeLabel }}</Badge>
      <Badge
        v-for="fact in propertyFacts(property)"
        :key="fact"
        variant="outline"
        class="h-auto max-w-full shrink justify-start font-normal wrap-anywhere whitespace-normal"
      >
        {{ fact }}
      </Badge>
    </div>
    <p v-if="'Description' in property && property.Description" class="mt-0.5 text-xs wrap-anywhere text-muted-foreground">
      {{ property.Description }}
    </p>
  </div>
</template>
