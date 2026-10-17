<script setup lang="ts">
import HelpItem from '@/components/HelpItem.vue'
import HelpSheet from '@/components/HelpSheet.vue'
</script>

<template>
  <HelpSheet
    title="About this entity"
    lead="One entity of your application: its properties, which are the columns of its table, its constraints and the order its records come back in."
  >
    <HelpItem title="What you can do here" open>
      <ul>
        <li>
          <strong>Properties</strong>: <strong>New property</strong> adds a column. The menu of a property edits its
          description, its pattern and its limits, renames it or deletes it.
        </li>
        <li>
          <strong>Constraints</strong>: add and remove unique values and foreign keys, then save them together.
        </li>
        <li><strong>Sorting</strong>: the order the API returns the records in when a request does not ask for one.</li>
        <li><strong>Data</strong> opens its records. <strong>Security</strong> opens the access rules for it.</li>
      </ul>
    </HelpItem>

    <HelpItem title="A new entity is private">
      <p>
        The API serves an entity only to the callers that a security rule allows. Until you add a rule on the Security tab,
        only the people who manage the application (its owner, its collaborators and the administrators of the instance),
        working in the Portal, can read or change its records.
      </p>
    </HelpItem>

    <HelpItem title="What the marks next to a property mean">
      <ul>
        <li>
          <strong>Unique</strong>: no two records have the same value. <strong>Unique with Phone</strong>: no two records
          have the same combination of this value and Phone.
        </li>
        <li>
          <strong>→ Customers</strong>: a foreign key. The property holds the ID of a record of Customers, and that record must
          exist. <strong>(cascade)</strong> or <strong>(set null)</strong> after it says what happens when that record is
          deleted.
        </li>
        <li>The icon at the start of the row says who may read or write the property. It opens its rules in Security.</li>
      </ul>
    </HelpItem>

    <HelpItem title="What cannot be changed">
      <p>
        The type, the decimal places, Required, Encrypted and the maximum length of text cannot be changed once the property
        exists. To change one, add a new property.
      </p>
      <p>
        The system properties (<code>ID</code>, <code>Owner</code> and <code>Created</code> of an entity you created) cannot
        be edited, renamed or deleted.
      </p>
    </HelpItem>

    <HelpItem title="Renaming and deleting a property">
      <p>
        A rename changes the name of the column. Custom endpoints that use the property keep the old name in their SQL, and
        history records that refer to it by the old name are no longer available. Deleting a property deletes its values in
        every record and cannot be undone.
      </p>
      <p>
        Neither is possible while the property is part of a constraint: remove the constraint on the Constraints tab first.
      </p>
    </HelpItem>

    <HelpItem title="Constraints">
      <p>Saving applies the changes to the table. It fails when existing records break a new constraint.</p>
      <p>
        A constraint the entity comes with cannot be removed. The constraints of a system entity can be changed only by an
        administrator.
      </p>
    </HelpItem>
  </HelpSheet>
</template>
