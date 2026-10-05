<script setup lang="ts">
import HelpItem from '@/components/HelpItem.vue'
import HelpSheet from '@/components/HelpSheet.vue'
</script>

<template>
  <HelpSheet
    title="About applications"
    lead="An application is the backend of one client app, such as a website or a mobile app. This page lists the applications you own and the ones shared with you."
  >
    <HelpItem title="What you can do here" open>
      <ul>
        <li>
          <strong>Open an application</strong> by clicking its name. Its tabs are Entities (the data model), Data (the
          records), Security, Custom endpoints, Email, Reports, Sharing (owner only), Import, Audit log and Settings.
        </li>
        <li>
          <strong>New application</strong> creates an application with only the system entities, on one of the servers of
          this instance.
        </li>
        <li><strong>Import application</strong> creates an application from the definition file of an exported one.</li>
        <li><strong>Search</strong> by name when the list gets long.</li>
      </ul>
    </HelpItem>

    <HelpItem title="What a card shows">
      <p>
        The status (an offline application refuses every call from its users), the database it uses, its differentiation
        entity (or none) and its size on disk. Your own applications also show how many users they are shared with;
        applications shared with you show their owner.
      </p>
    </HelpItem>

    <HelpItem title="What the card menu does">
      <ul>
        <li>
          <strong>Info</strong>: the address of the API server, the application token and the encryption key. A client app
          needs the address and the token to call the API.
        </li>
        <li><strong>API</strong>: the Swagger page of the API server, to try the calls of this application.</li>
        <li>
          <strong>Compare with…</strong>: what differs between this application and another one you can open: entities,
          properties, constraints, custom endpoints and security rules.
        </li>
        <li><strong>Clone</strong>: a copy of the application, with or without its records.</li>
        <li>
          <strong>Export</strong>: downloads a zip of the application. Its definition, <code>application.json</code>, comes
          without the mail settings, the connection string and the sharing. For an application on SQLite, the zip also holds
          the database file and the uploaded files, so handle it like the data itself.
        </li>
        <li><strong>Settings</strong>: the name, connection string and status of the application.</li>
        <li>
          <strong>Rebuild</strong> and <strong>Delete</strong> cannot be undone. Rebuild drops all the data and creates the
          tables again, empty. Delete removes the application and all its data for good.
        </li>
      </ul>
    </HelpItem>

    <HelpItem title="Applications shared with you">
      <p>
        You can manage them like your own: a collaborator can do everything the owner can, except share the application
        with others.
      </p>
    </HelpItem>
  </HelpSheet>
</template>
