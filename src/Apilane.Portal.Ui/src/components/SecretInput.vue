<script setup lang="ts">
import { EyeIcon, EyeOffIcon } from '@lucide/vue'
import { ref, useId } from 'vue'
import { Button } from '@/components/ui/button'
import { Checkbox } from '@/components/ui/checkbox'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'

// The input for a secret the API never sends back (a password, a key). The box starts empty and
// a hint says whether a value is stored; leaving it empty keeps that value, so send null then:
//
//   <FormField v-slot="{ field }" label="Mail password" :error="errors.fields.MailPassword">
//     <SecretInput v-model="form.MailPassword" v-bind="field" :is-set="settings.HasMailPassword" />
//   </FormField>
//
//   body: { MailPassword: form.MailPassword || null }
//
// For a secret that may be removed, add `removable` and bind v-model:remove. A 'Remove the stored
// value' checkbox appears; send an empty string when it is ticked.
defineOptions({ inheritAttrs: false })

defineProps<{
  /** Whether a value is stored (the Has... flag of the response). */
  isSet: boolean
  /** Whether the stored value may be removed. Needs v-model:remove. */
  removable?: boolean
}>()

const value = defineModel<string>({ required: true })
const remove = defineModel<boolean>('remove', { default: false })

const visible = ref(false)
const removeId = useId()
const hintId = useId()
</script>

<template>
  <div class="grid gap-1.5">
    <div class="flex gap-1.5">
      <!-- 'new-password' stops the browser from filling in the user's own login password. -->
      <Input
        v-model="value"
        v-bind="$attrs"
        :aria-describedby="[$attrs['aria-describedby'], hintId].filter(Boolean).join(' ')"
        :type="visible ? 'text' : 'password'"
        :disabled="remove"
        :placeholder="isSet ? 'Leave empty to keep the stored value' : undefined"
        autocomplete="new-password"
        spellcheck="false"
      />
      <Button type="button" variant="outline" size="icon" :title="visible ? 'Hide' : 'Show'" :aria-pressed="visible" @click="visible = !visible">
        <EyeOffIcon v-if="visible" />
        <EyeIcon v-else />
        <span class="sr-only">Show what is typed</span>
      </Button>
    </div>

    <p :id="hintId" class="text-xs text-muted-foreground">
      <template v-if="isSet">A value is set. It is not shown; leave the box empty to keep it.</template>
      <template v-else>Not set.</template>
    </p>

    <div v-if="isSet && removable" class="flex items-center gap-2">
      <Checkbox :id="removeId" :model-value="remove" @update:model-value="(checked) => (remove = checked === true)" />
      <Label :for="removeId" class="text-xs font-normal">Remove the stored value</Label>
    </div>
  </div>
</template>
