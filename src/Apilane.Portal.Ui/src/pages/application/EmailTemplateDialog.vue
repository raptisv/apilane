<script setup lang="ts">
import { computed, reactive, ref, watch } from 'vue'
import FormDialog from '@/components/FormDialog.vue'
import FormField from '@/components/FormField.vue'
import HtmlPreview from '@/components/HtmlPreview.vue'
import SwitchField from '@/components/SwitchField.vue'
import { Input } from '@/components/ui/input'
import { Textarea } from '@/components/ui/textarea'
import { useApplication } from '@/composables/useApplication'
import { useMutation } from '@/composables/useMutation'
import { apiServer } from '@/lib/apiServer'
import { templatePlaceholders, templateProblems } from '@/lib/emailTemplates'
import type { EmailTemplate, EmailTemplateUpdate } from '@/lib/emailTemplates'
import { formErrors } from '@/lib/forms'
import * as toast from '@/lib/toast'

// Edits one e-mail template. It is saved on the application's API server:
// PUT {ServerUrl}/api/Email/Update.
const props = defineProps<{ template: EmailTemplate }>()
const emit = defineEmits<{ saved: [] }>()

const open = defineModel<boolean>('open', { required: true })

const { application } = useApplication()

const form = reactive({ Active: false, Subject: '', Content: '' })

// Checked here before sending; see templateProblems.
const problems = ref<Record<string, string>>({})

const save = useMutation((body: EmailTemplateUpdate) =>
  apiServer(application.value.Server.ServerUrl, application.value.Token).put('/api/Email/Update', body),
)

// The API server names the field it rejects (Subject); that field is marked.
const errors = computed(() => {
  const result = formErrors(save.error.value, ['Active', 'Subject', 'Content'])

  return { fields: { ...result.fields, ...problems.value }, message: result.message }
})

const placeholders = computed(() => templatePlaceholders(props.template.EventCode ?? ''))

// Fill the form every time the dialog opens, so Cancel throws the edits away.
watch(
  open,
  (isOpen) => {
    if (isOpen) {
      form.Active = props.template.Active
      form.Subject = props.template.Subject ?? ''
      form.Content = props.template.Content ?? ''
      problems.value = {}
      save.reset()
    }
  },
  { immediate: true },
)

async function submit(): Promise<boolean> {
  problems.value = templateProblems(form)

  if (Object.keys(problems.value).length > 0) {
    save.reset()
    return false
  }

  const saved = await save.run({
    ID: props.template.ID,
    EventCode: props.template.EventCode ?? '',
    Active: form.Active,
    Subject: form.Subject,
    Content: form.Content,
  })

  if (saved) {
    toast.success('Email template saved.')
    emit('saved')
  }

  return saved
}
</script>

<template>
  <FormDialog
    v-model:open="open"
    wide
    :title="`Edit email: ${template.Description}`"
    :submit="submit"
    :error="errors.message"
  >
    <SwitchField
      v-model="form.Active"
      label="Enabled"
      help="A disabled email is not sent."
      :error="errors.fields.Active"
    />

    <FormField v-slot="{ field }" label="Subject" :error="errors.fields.Subject">
      <Input v-model="form.Subject" v-bind="field" autocomplete="off" />
    </FormField>

    <div class="grid gap-4 lg:grid-cols-2">
      <FormField v-slot="{ field }" label="Body (HTML)" :error="errors.fields.Content">
        <Textarea
          v-model="form.Content"
          v-bind="field"
          class="h-72 field-sizing-fixed font-mono md:text-xs"
          spellcheck="false"
          autocomplete="off"
        />
      </FormField>

      <div class="grid content-start gap-1.5">
        <p class="text-sm font-medium" aria-hidden="true">Preview</p>
        <HtmlPreview :html="form.Content" title="Preview of the email body" />
        <p class="text-xs text-muted-foreground">Scripts in the body do not run here. The placeholders are not filled in.</p>
      </div>
    </div>

    <section v-if="placeholders.length > 0" class="grid gap-2 border-t pt-4" aria-labelledby="placeholders-title">
      <h3 id="placeholders-title" class="text-sm font-medium">Placeholders you can use in the subject and body</h3>
      <dl class="grid gap-1.5 text-sm sm:grid-cols-[auto_1fr] sm:gap-x-4">
        <template v-for="placeholder in placeholders" :key="placeholder.name">
          <dt class="font-mono text-xs break-all sm:pt-0.5">{{ placeholder.name }}</dt>
          <dd class="mb-1.5 text-muted-foreground sm:mb-0">{{ placeholder.description }}</dd>
        </template>
      </dl>
    </section>
  </FormDialog>
</template>
