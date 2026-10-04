<script setup lang="ts">
import { InfoIcon, TriangleAlertIcon } from '@lucide/vue'
import { computed } from 'vue'
import { Input } from '@/components/ui/input'
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'
import { databaseTypeLabel, databaseTypes, needsConnectionString } from '@/lib/applications'
import FormField from './FormField.vue'
import SecretInput from './SecretInput.vue'

// The two fields that say where an application's data lives: the database type and, for every
// type except SQLite, the connection string with the notes for that type. Use it in every form
// that puts an application on a database (new, import, clone, edit):
//
//   <DatabaseTypeFields
//     v-model:database-type="form.DatabaseType"
//     v-model:connection-string="form.ConnectionString"
//     :database-type-error="errors.fields.DatabaseType"
//     :connection-string-error="errors.fields.ConnectionString"
//   />
//
// The connection string is a secret. The API never sends it back, so the box always starts empty.
// Keep it in the page's own state only (never the address, storage or a shared module), so it is
// gone when the page is left. Send it only when needsConnectionString(databaseType).
//
// For an application that exists (the edit form) add `existing` and `has-connection-string`: the
// database type is shown disabled, since it cannot be changed, and the connection string becomes a
// SecretInput that says whether one is stored; an empty box keeps it (connectionStringToSend).
defineProps<{
  databaseTypeError?: string
  connectionStringError?: string
  /** The application exists: the type is read-only and the connection string write-only. */
  existing?: boolean
  /** With `existing`: whether a connection string is stored (HasConnectionString). */
  hasConnectionString?: boolean
  /** Replaces the sentence that says when the connection string is checked. For a form that does not check it on submit (clone). */
  checkNote?: string
}>()

const databaseType = defineModel<string>('databaseType', { required: true })
const connectionString = defineModel<string>('connectionString', { required: true })

// The notes of the classic portal, per database type. `warning` is [before, emphasised, after].
const notes: Record<string, { examplesUrl: string; examplesLabel: string; warning: [string, string, string] }> = {
  SQLServer: {
    examplesUrl: 'https://www.connectionstrings.com/sql-server/',
    examplesLabel: 'SQL Server connection string examples',
    warning: ['Remember to use ', 'TrustServerCertificate=true;', ' in case there is no specific certificate used for the connection.'],
  },
  MySQL: {
    examplesUrl: 'https://www.connectionstrings.com/mysql/',
    examplesLabel: 'MySQL connection string examples',
    warning: ['Do not forget to use ', 'UseXaTransactions=false;', ' to allow queries execute inside TransactionScope.'],
  },
  PostgreSQL: {
    examplesUrl: 'https://www.connectionstrings.com/postgresql/',
    examplesLabel: 'PostgreSQL connection string examples',
    warning: ['Ensure the database user has ', 'CREATE TABLE', ' privileges on the target database.'],
  },
}

const note = computed(() => notes[databaseType.value])
</script>

<template>
  <FormField
    v-slot="{ field }"
    label="Database type"
    :help="existing ? 'The database type cannot be changed.' : undefined"
    :error="databaseTypeError"
  >
    <Select v-model="databaseType" :disabled="existing">
      <SelectTrigger v-bind="field" class="w-full">
        <SelectValue />
      </SelectTrigger>
      <SelectContent>
        <SelectItem v-for="type in databaseTypes" :key="type" :value="type">{{ databaseTypeLabel(type) }}</SelectItem>
      </SelectContent>
    </Select>
  </FormField>

  <template v-if="needsConnectionString(databaseType)">
    <FormField v-slot="{ field }" label="Connection string" :error="connectionStringError">
      <SecretInput
        v-if="existing"
        v-model="connectionString"
        v-bind="field"
        class="font-mono"
        :is-set="Boolean(hasConnectionString)"
      />
      <Input v-else v-model="connectionString" v-bind="field" class="font-mono" autocomplete="off" spellcheck="false" />
    </FormField>

    <template v-if="note">
      <div class="flex gap-2.5 rounded-lg border bg-muted/40 p-3 text-sm">
        <InfoIcon class="mt-0.5 size-4 shrink-0 text-link" aria-hidden="true" />
        <p>
          <template v-if="existing">
            A new connection string moves no data: it must point to the database that already holds this
            application's tables. It is not checked when you save; a wrong value shows when the application is next
            called.
          </template>
          <template v-else>
            The connection string must be pointing to an existing but empty database with the necessary access rights.
            {{ checkNote ?? "The connection string will be validated as soon as you click on 'save'." }}
          </template>
          <a
            :href="note.examplesUrl"
            target="_blank"
            rel="noopener"
            class="rounded-sm text-link underline-offset-4 hover:underline focus-visible:outline-2 focus-visible:outline-ring"
          >
            {{ note.examplesLabel }}
            <span class="sr-only">(opens in a new tab)</span>
          </a>
        </p>
      </div>

      <div class="flex gap-2.5 rounded-lg border border-warning/40 bg-warning/10 p-3 text-sm text-warning">
        <TriangleAlertIcon class="mt-0.5 size-4 shrink-0" aria-hidden="true" />
        <p class="wrap-anywhere">
          {{ note.warning[0] }}<strong>{{ note.warning[1] }}</strong>{{ note.warning[2] }}
        </p>
      </div>
    </template>
  </template>
</template>
