<script setup lang="ts">
import HelpItem from '@/components/HelpItem.vue'
import HelpSheet from '@/components/HelpSheet.vue'

// The log of one application, or the one of the instance itself (administration).
defineProps<{ scope: 'application' | 'instance' }>()
</script>

<template>
  <HelpSheet
    title="About the audit log"
    :lead="
      scope === 'instance'
        ? 'A record of who changed what on this instance, kept so that you can answer \'who did that, and when?\'.'
        : 'A record of who changed what in this application in the Portal, kept so that you can answer \'who did that, and when?\'.'
    "
  >
    <HelpItem title="What you can do here" open>
      <ul>
        <li>Read the entries, <strong>most recent first</strong>, and page through the older ones.</li>
        <li>
          Click an entry, or its arrow, to <strong>open its details</strong>: for a modified entry, each property that
          changed, with its old and new value; for a created or deleted one, the values it was created with or had.
        </li>
        <li>You cannot edit or delete entries. The log is read-only.</li>
      </ul>
    </HelpItem>

    <HelpItem title="What an entry says">
      <ul>
        <li><strong>Timestamp</strong>: when it happened, in UTC.</li>
        <li><strong>User</strong>: who did it.</li>
        <li><strong>Action</strong>: created (green), modified (yellow) or deleted (red).</li>
        <li><strong>Type</strong> and <strong>target</strong>: what kind of thing it was, and which one.</li>
      </ul>
    </HelpItem>

    <HelpItem v-if="scope === 'application'" title="What is logged">
      <p>
        Changes made in the Portal to this application: its settings (SMTP settings included), entities, properties,
        constraints, security rules, custom endpoints, reports and sharing.
      </p>
      <p>
        Not here: the records of the application, whoever changes them, the e-mail templates and a rebuild. For the history
        of a record, switch on record change tracking for its entity.
      </p>
    </HelpItem>

    <HelpItem v-else title="What is logged">
      <p>
        Changes to the instance itself: servers, instance settings, user roles, new applications (with their first entities
        and properties) and backups of the Portal database. The changes inside an application are in its own audit log, on
        its Audit log tab.
      </p>
    </HelpItem>

    <HelpItem title="Secrets are not shown">
      <p>Passwords, keys and connection strings are logged as <code>***</code>. The log shows that one changed, never its value.</p>
    </HelpItem>
  </HelpSheet>
</template>
