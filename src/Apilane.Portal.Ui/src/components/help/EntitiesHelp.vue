<script setup lang="ts">
import HelpItem from '@/components/HelpItem.vue'
import HelpSheet from '@/components/HelpSheet.vue'
</script>

<template>
  <HelpSheet
    title="About entities"
    lead="Entities are the tables of your application. Each one holds one kind of record, such as Products or Orders, and its properties are the columns."
  >
    <HelpItem title="What you can do here" open>
      <ul>
        <li><strong>New entity</strong> adds a table for a new kind of record.</li>
        <li>
          The menu of each row opens its <strong>Properties</strong> (the columns), <strong>Constraints</strong> (unique
          values and links to other entities) and <strong>Default sorting</strong>.
        </li>
        <li><strong>Data</strong> opens its records. <strong>Security</strong> opens the access rules for it.</li>
        <li><strong>Edit</strong> changes its description and record change tracking. <strong>Rename</strong> and
          <strong>Delete</strong> change the table itself.</li>
      </ul>
    </HelpItem>

    <HelpItem title="A new entity is private">
      <p>
        The API serves an entity only to the callers that a security rule allows. Until you add a rule on the Security tab,
        only the people who manage the application (its owner, its collaborators and the administrators of the instance),
        working in the Portal, can read or change its records.
      </p>
    </HelpItem>

    <HelpItem title="Properties">
      <p>
        A property is a column. Its type is String (text), Number (whole or with decimals), Boolean or Date. It can be
        required, and it can have limits: a minimum and a maximum length for text, or a minimum and a maximum value for a
        number. A text property can also be encrypted or checked against a regular expression.
      </p>
      <p>
        The type, the decimal places, Required, Encrypted and the maximum length of text cannot be changed once the property
        exists. To change one, add a new property.
      </p>
    </HelpItem>

    <HelpItem title="Constraints">
      <p>
        A <strong>unique</strong> constraint stops two records from having the same value in a property, or the same
        combination of values in several. A <strong>foreign key</strong> makes a property hold the ID of a record of another
        entity, and that record must exist.
      </p>
      <p>
        For a foreign key you also choose what happens when the record it points to is deleted: refuse the delete, set the
        property to null, or delete the records that point to it too.
      </p>
    </HelpItem>

    <HelpItem title="Default sorting">
      <p>
        The order in which the API returns the records of an entity when the caller does not ask for one. Without it,
        records come back by ID, ascending.
      </p>
    </HelpItem>

    <HelpItem title="System entities and properties">
      <p>
        The system entities (Users, AuthTokens and Files, plus the differentiation entity if there is one) exist from the
        start and cannot be renamed or deleted. Every entity you create also has three properties of its own:
        <code>ID</code> (the primary key), <code>Owner</code> (the user who created the record) and <code>Created</code>
        (when).
      </p>
    </HelpItem>

    <HelpItem title="Record change tracking">
      <p>
        Keeps a history record of every update and delete of a record. You can browse and delete the history in the data browser. It
        slows updates down noticeably, so use it only for entities that are not updated often.
      </p>
    </HelpItem>

    <HelpItem title="Has differentiation property">
      <p>
        Offered only when the application has a differentiation entity, and only when you create the entity. Each record then
        belongs to one record of the differentiation entity, and a user sees only the records that belong to theirs. It
        cannot be changed later.
      </p>
    </HelpItem>

    <HelpItem title="Renaming and deleting">
      <p>
        A rename changes the name of the table and the address of the entity in the API. Security rules, custom endpoints
        and reports that name the entity are not changed, so update them yourself.
      </p>
      <p>
        Deleting an entity deletes all its records and cannot be undone. Neither a rename nor a delete is possible while a
        foreign key of any entity points to it: remove that constraint first. System entities can be neither renamed nor
        deleted.
      </p>
    </HelpItem>
  </HelpSheet>
</template>
