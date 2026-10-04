<script setup lang="ts">
import { useId } from 'vue'
import { Label } from '@/components/ui/label'
import { Switch } from '@/components/ui/switch'

// An on/off choice of a form: the switch with its label next to it, help text and the field's
// error underneath. The counterpart of FormField for a boolean.
//
//   <SwitchField v-model="form.RequireChangeTracking" label="Record change tracking" help="..."
//     :error="errors.fields.RequireChangeTracking" />
defineProps<{
  label: string
  /** A line under the switch on what it does. */
  help?: string
  /** The field's message from formErrors() (lib/forms.ts). */
  error?: string
}>()

const checked = defineModel<boolean>({ required: true })

const id = useId()
const helpId = `${id}-help`
const errorId = `${id}-error`
</script>

<template>
  <div class="grid gap-1.5">
    <div class="flex items-center gap-2.5">
      <Switch
        :id="id"
        v-model="checked"
        :aria-invalid="error ? true : undefined"
        :aria-describedby="[error && errorId, help && helpId].filter(Boolean).join(' ') || undefined"
      />
      <Label :for="id">{{ label }}</Label>
    </div>
    <p v-if="help" :id="helpId" class="text-xs text-muted-foreground">{{ help }}</p>
    <p v-if="error" :id="errorId" role="alert" class="text-xs text-destructive">{{ error }}</p>
  </div>
</template>
