<script setup lang="ts">
import HelpItem from '@/components/HelpItem.vue'
import HelpSheet from '@/components/HelpSheet.vue'

// The administrator's browser opens the data of any application of the instance, not only their own.
defineProps<{ admin?: boolean }>()
</script>

<template>
  <HelpSheet
    title="About the data browser"
    :lead="
      admin
        ? 'Browse and change the records of one application of this instance, with the same tools its owner has.'
        : 'Browse, filter and change the records of this application, without writing any SQL.'
    "
  >
    <HelpItem title="What you can do here" open>
      <ul>
        <li><strong>Pick an entity</strong> in the list to see its records.</li>
        <li v-if="!admin"><strong>Properties</strong>, next to this Help, opens the columns of the entity on screen.</li>
        <li>
          <strong>Sort</strong> by a column by clicking its heading, and <strong>filter</strong> by typing in the boxes
          under the headings. Both work on all the records of the entity, not only the page on screen.
        </li>
        <li>
          <strong>Add</strong> a record with the button above the grid: New record, Register user for the Users entity
          (only while registration is allowed on the Security tab), Upload for Files.
        </li>
        <li>
          <strong>Edit</strong> or <strong>delete</strong> a record with the buttons at the start of its row. Tick records
          and use <strong>Delete selected</strong> to remove several at once.
        </li>
        <li><strong>History</strong>, a button on the row, shows the earlier versions of a record.</li>
        <li>
          The <strong>More actions</strong> menu above the grid has <strong>Export page as CSV</strong>, which saves the rows
          on screen as a file, and <strong>Clear history</strong>.
        </li>
      </ul>
    </HelpItem>

    <HelpItem title="Security rules do not apply here">
      <p>
        {{ admin ? 'As an administrator' : 'As the owner or a collaborator of the application' }} you are not held back by
        the security rules or the rate limits that apply to its users, so you see every record of every entity. The checks
        on the data itself still run: required values, limits, unique values and foreign keys.
      </p>
      <p>
        To see what a user sees, call the API as that user. The access rules on the Security tab decide what they get.
      </p>
    </HelpItem>

    <HelpItem title="Filters">
      <ul>
        <li>
          <strong>Text</strong>: contains the text. The button in the box switches it to <em>equal to</em>. An encrypted
          property is stored encrypted and the filter is compared with that stored text, so a readable value does not match
          it.
        </li>
        <li>
          <strong>Numbers</strong>: equal to. The button in the box switches it to <em>at least</em> (≥) and
          <em>at most</em> (≤).
        </li>
        <li>
          <strong>Dates</strong>: typed as <code>yyyy-MM-dd</code>, with <code>HH:mm</code> or <code>HH:mm:ss</code> after it
          if you like, and read as UTC. A day alone matches the whole day, and a time the whole minute or second. The button
          switches it to <em>on or after</em> (≥) and <em>on or before</em> (≤), which take in that day, minute or second too.
        </li>
        <li><strong>True or false</strong>: choose Any, true or false.</li>
      </ul>
      <p>
        Filters of different columns combine: a record must match all of them. They apply a moment after you stop typing.
      </p>
    </HelpItem>

    <HelpItem title="Which actions are offered">
      <p>
        Only what the entity allows. A file cannot be edited: upload a new one or delete the old one. History appears only
        for entities with record change tracking.
      </p>
    </HelpItem>

    <HelpItem title="Sharing a view">
      <p>
        The entity, the sort and the page size are part of the address, so you can bookmark a view or send it to someone who
        can open the application. The filters are not.
      </p>
    </HelpItem>
  </HelpSheet>
</template>
