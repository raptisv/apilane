<script setup lang="ts">
import { CircleHelpIcon } from '@lucide/vue'
import { Button } from '@/components/ui/button'
import { Sheet, SheetContent, SheetDescription, SheetHeader, SheetTitle, SheetTrigger } from '@/components/ui/sheet'

// The help of a screen, one click from its header, so it never takes room from the screen itself. The lead says
// what the screen is for; the HelpItem list below it says what a person can do there and answers what they are
// likely to ask next. The words of each screen live in components/help.
// The button is as tall as the buttons of a screen header; a smaller header (a section's) asks for size="sm".
defineProps<{
  title: string
  lead: string
  size?: 'default' | 'sm'
}>()
</script>

<template>
  <Sheet>
    <SheetTrigger as-child>
      <Button variant="outline" :size="size ?? 'default'">
        <CircleHelpIcon />
        Help
      </Button>
    </SheetTrigger>
    <SheetContent class="w-full gap-0 data-[side=right]:w-full data-[side=right]:sm:max-w-lg">
      <SheetHeader class="border-b pr-12">
        <SheetTitle>{{ title }}</SheetTitle>
        <SheetDescription class="text-sm leading-relaxed text-foreground/80">{{ lead }}</SheetDescription>
      </SheetHeader>
      <div class="min-h-0 flex-1 overflow-y-auto p-4">
        <div class="divide-y rounded-lg border bg-card">
          <slot />
        </div>
      </div>
    </SheetContent>
  </Sheet>
</template>
