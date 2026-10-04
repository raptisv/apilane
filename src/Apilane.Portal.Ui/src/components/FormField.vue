<script setup lang="ts">
import { computed, useId } from 'vue'
import { Label } from '@/components/ui/label'

// One labelled control of a form: label, the control, help text, and the field's error message.
// Bind the slot's `field` onto the control so the label, help and error are tied to it:
//
//   <FormField v-slot="{ field }" label="Name" :error="errors.fields.Name">
//     <Input v-model="form.Name" v-bind="field" />
//   </FormField>
const props = defineProps<{
  label: string
  /** A line under the control on what to enter. */
  help?: string
  /** The field's message from formErrors() (lib/forms.ts). */
  error?: string
}>()

const id = useId()
const helpId = `${id}-help`
const errorId = `${id}-error`

const field = computed(() => ({
  id,
  'aria-invalid': props.error ? true : undefined,
  'aria-describedby': [props.error && errorId, props.help && helpId].filter(Boolean).join(' ') || undefined,
}))
</script>

<template>
  <div class="grid gap-1.5">
    <Label :for="id">{{ label }}</Label>
    <slot :field="field" />
    <p v-if="help" :id="helpId" class="text-xs text-muted-foreground">{{ help }}</p>
    <p v-if="error" :id="errorId" role="alert" class="text-xs text-destructive">{{ error }}</p>
  </div>
</template>
