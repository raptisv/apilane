<script setup lang="ts">
import HelpItem from '@/components/HelpItem.vue'
import HelpSheet from '@/components/HelpSheet.vue'
</script>

<template>
  <HelpSheet
    title="About application settings"
    lead="The basics of this application: its name, the database it connects to, and whether it is available. It is also where you rebuild or delete it."
  >
    <HelpItem title="What you can do here" open>
      <ul>
        <li><strong>Rename</strong> the application.</li>
        <li><strong>Change the connection string</strong> of its database, when it uses SQL Server, MySQL or PostgreSQL.</li>
        <li><strong>Take it offline</strong>, or bring it back online.</li>
        <li><strong>Rebuild</strong> or <strong>delete</strong> it, in the danger zone.</li>
      </ul>
    </HelpItem>

    <HelpItem title="What is fixed">
      <p>
        The server and the database type cannot be changed once the application exists, and neither can its differentiation
        entity. To move an application to another server or database, clone it there.
      </p>
    </HelpItem>

    <HelpItem title="Connection string">
      <p>
        Never shown again once saved. Leave its box empty to keep the one that is stored, or type a new one to replace it.
        Change it when the database moves, or its password does. Point it at the same data: Apilane does not copy anything
        from the old database to the new one.
      </p>
    </HelpItem>

    <HelpItem title="Status: online and offline">
      <p>
        An <strong>offline</strong> application is not available to its users: the API server refuses their calls. The
        Portal still works for its owner, its collaborators and the administrators. Use it to stop traffic during
        maintenance. Nothing is deleted: bring it back online and it is available again.
      </p>
    </HelpItem>

    <HelpItem title="Rebuild">
      <p>
        Deletes all the data and creates the tables again, empty. The token stays the same, and so do the entities and
        properties. On SQL Server, MySQL and PostgreSQL it drops every table of the connected database, not only the ones of
        Apilane, so give the application a database of its own. Use it when you want to start over with the same
        structure, or when the tables no longer match the definition, for example after a custom endpoint dropped one. It
        cannot be undone, and you confirm it the same way as a delete.
      </p>
    </HelpItem>

    <HelpItem title="Delete">
      <p>
        Removes the application for good: its data and files on the API server (on SQL Server, MySQL and PostgreSQL every
        table of the connected database is dropped, and the database itself stays), and its entities, properties, custom
        endpoints, reports and sharing. It cannot be undone, and you confirm it by ticking a box and typing its name. Import
        application restores only the structure of an export, not its records or files, so clone the application with its
        data first if you may want them back. A clone never copies files.
      </p>
    </HelpItem>
  </HelpSheet>
</template>
