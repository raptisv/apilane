<script setup lang="ts">
import { CheckIcon, CopyIcon, EyeIcon, EyeOffIcon } from '@lucide/vue'
import { useClipboard } from '@vueuse/core'
import { ref, useId } from 'vue'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'

// A read-only value with a copy button, for something the user pastes elsewhere (an address, a
// token, a key). With `secret` the value is masked until the user asks to see it; copying works
// either way.
//
//   <CopyField label="Token" :value="application.Token" />
//   <CopyField label="Encryption key" :value="key" secret />
const props = defineProps<{
  label: string
  value: string
  /** Mask the value and add a show/hide button. */
  secret?: boolean
}>()

const id = useId()
const visible = ref(false)

// `legacy` falls back to the old copy command where the clipboard API is not available (plain http).
const { copy, copied } = useClipboard({ copiedDuring: 1200, legacy: true })

function selectAll(event: FocusEvent): void {
  ;(event.target as HTMLInputElement).select()
}
</script>

<template>
  <div class="grid gap-1.5">
    <Label :for="id">{{ label }}</Label>
    <div class="flex gap-1.5">
      <Input
        :id="id"
        :model-value="props.value"
        :type="secret && !visible ? 'password' : 'text'"
        readonly
        autocomplete="off"
        spellcheck="false"
        class="font-mono text-xs md:text-xs"
        @focus="selectAll"
      />
      <Button
        v-if="secret"
        type="button"
        variant="outline"
        size="icon"
        :title="visible ? 'Hide' : 'Show'"
        :aria-pressed="visible"
        @click="visible = !visible"
      >
        <EyeOffIcon v-if="visible" />
        <EyeIcon v-else />
        <span class="sr-only">Show {{ label }}</span>
      </Button>
      <Button type="button" variant="outline" size="icon" :title="copied ? 'Copied' : 'Copy'" @click="copy(props.value)">
        <CheckIcon v-if="copied" class="text-success" />
        <CopyIcon v-else />
        <span class="sr-only">Copy {{ label }}</span>
      </Button>
    </div>
    <span class="sr-only" role="status">{{ copied ? `${label} copied` : '' }}</span>
  </div>
</template>
