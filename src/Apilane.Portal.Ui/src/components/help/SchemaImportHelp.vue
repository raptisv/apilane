<script setup lang="ts">
import HelpItem from '@/components/HelpItem.vue'
import HelpSheet from '@/components/HelpSheet.vue'

// The example payload is the one of lib/schemaImport, which the import's tests keep valid.
defineProps<{ example: string }>()

const codeClass = 'rounded-sm bg-muted px-1 font-mono text-xs text-foreground'
</script>

<template>
  <HelpSheet
    title="About importing a schema"
    lead="Adds entities, properties, constraints, security rules and custom endpoints to this application from a JSON payload. Use it to bring a structure over from another application, or to set one up from a file."
  >
    <HelpItem title="What you can do here" open>
      <ol>
        <li>
          <strong>Load from another application</strong>: choose one you can open and click Load diff. The payload box then
          holds everything that application has and this one does not.
        </li>
        <li>Or paste a payload of your own, or write one. You can edit it before importing.</li>
        <li>Click <strong>Import</strong> and confirm. The result is shown under the box.</li>
      </ol>
    </HelpItem>

    <HelpItem title="How the import works">
      <ul>
        <li>The items of the payload are added to the application one by one.</li>
        <li>
          Entities without a foreign key are processed first. A referenced entity must already exist or appear in the same
          payload: list it before the entities whose foreign keys point to it.
        </li>
        <li>
          If an entity or property already exists, its definition is checked against the payload. The import stops at the
          first mismatch.
        </li>
        <li>A custom endpoint whose name already exists is skipped, whatever its query.</li>
        <li>
          Security rules are identified by <code :class="codeClass">TypeID + Name + RoleID + Action</code>. Identical entries
          are skipped; differing ones cause an error.
        </li>
      </ul>
    </HelpItem>

    <HelpItem title="It cannot be undone">
      <p>
        The import is not atomic: when a step fails, the steps before it stay applied. Importing the same payload again is
        safe, because what is already there is skipped, so after a failure fix the payload and run it again.
      </p>
    </HelpItem>

    <HelpItem title="Import schema or import application?">
      <p>
        <strong>Import application</strong> creates a new application from an export. <strong>Import schema</strong> adds to
        one that already exists: it can add properties and constraints to existing entities and add security rules, but it
        never changes or removes what is already defined.
      </p>
    </HelpItem>

    <HelpItem title="Payload reference">
      <ul>
        <li>Property <code :class="codeClass">TypeID</code>: 1 = String, 2 = Number, 3 = Boolean, 4 = Date.</li>
        <li>
          Constraint <code :class="codeClass">TypeID</code>: 1 = Unique, 2 = Foreign key. A foreign key lists its
          <code :class="codeClass">Properties</code> as <code :class="codeClass">LocalColumn,FKEntity</code>.
        </li>
        <li>Security <code :class="codeClass">TypeID</code>: 0 = Entity, 1 = Custom endpoint, 2 = Schema.</li>
        <li>
          Security <code :class="codeClass">RoleID</code>: <code :class="codeClass">ANONYMOUS</code>,
          <code :class="codeClass">AUTHENTICATED</code>, or the name of a role.
        </li>
        <li>
          Security <code :class="codeClass">Record</code>: 0 = All records, 1 = Owned records only.
          <code :class="codeClass">RateLimit</code> is null or
          <code :class="codeClass">{"MaxRequests": 10, "TimeWindowType": 2}</code>, where the window is 1 = per second, 2 = per
          minute, 3 = per hour.
        </li>
      </ul>
    </HelpItem>

    <HelpItem title="Example payload">
      <pre class="overflow-x-auto rounded-md bg-muted/50 p-3 font-mono text-xs">{{ example }}</pre>
    </HelpItem>
  </HelpSheet>
</template>
