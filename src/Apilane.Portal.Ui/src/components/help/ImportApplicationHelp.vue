<script setup lang="ts">
import HelpItem from '@/components/HelpItem.vue'
import HelpSheet from '@/components/HelpSheet.vue'
</script>

<template>
  <HelpSheet
    title="About importing an application"
    lead="Creates an application from the definition file of an exported one. Use it to move an application to another server or instance, or to start from a saved copy."
  >
    <HelpItem title="What you can do here" open>
      <ol>
        <li>Export the source application: the menu of its card on the Applications page, then Export. You get a zip.</li>
        <li>Unzip it and choose the file <code>application.json</code> in the File box.</li>
        <li>Pick the server and the database type here. SQL Server, MySQL and PostgreSQL also need a connection string.</li>
        <li>Click Save.</li>
      </ol>
    </HelpItem>

    <HelpItem title="What is imported">
      <p>
        The definition of the application: its entities with their properties and constraints, custom endpoints, security
        rules and reports, together with its application token and encryption key.
      </p>
      <p>
        Not imported: records, uploaded files, mail settings, the connection string and sharing. The import reads only
        <code>application.json</code> and ignores the rest of the zip, and the export blanks the mail settings, the
        connection string and sharing in that file.
      </p>
    </HelpItem>

    <HelpItem title="Why does the token matter?">
      <p>
        The import keeps the token and the encryption key of the file, so a client app that already knows the token keeps
        working. If an application with that token already exists, change the <code>Token</code> in
        <code>application.json</code> to a new one before you upload it.
      </p>
    </HelpItem>

    <HelpItem title="Import or clone?">
      <p>
        <strong>Clone</strong> copies an application you can already open here, with its records if you want them.
        <strong>Import</strong> starts from a file: another instance, a backup, or a template kept in your repository.
      </p>
    </HelpItem>
  </HelpSheet>
</template>
