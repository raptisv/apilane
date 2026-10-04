<script setup lang="ts">
import { Toaster } from '@/components/ui/sonner'
import { TooltipProvider } from '@/components/ui/tooltip'
</script>

<template>
  <!-- TooltipProvider and Toaster exist once, here: screens use Tooltip and lib/toast.ts directly. -->
  <TooltipProvider>
    <RouterView />
  </TooltipProvider>
  <Toaster theme="dark" position="bottom-right" />
</template>
