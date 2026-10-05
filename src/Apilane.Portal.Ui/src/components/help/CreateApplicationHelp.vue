<script setup lang="ts">
import HelpItem from '@/components/HelpItem.vue'
import HelpSheet from '@/components/HelpSheet.vue'
</script>

<template>
  <HelpSheet
    title="About new applications"
    lead="Creates an application: the backend of one client app. Choose where it runs and where its data lives, then build its data model on the next screen."
  >
    <HelpItem title="What you can do here" open>
      <ul>
        <li>Give it a <strong>name</strong>, 4 to 100 characters.</li>
        <li>Pick the <strong>server</strong> that will run it.</li>
        <li>
          Pick the <strong>database type</strong>. SQL Server, MySQL and PostgreSQL also need a connection string.
        </li>
        <li>Optionally name a <strong>differentiation entity</strong>, to keep groups of users apart.</li>
        <li>Click Save: the application is created and opens on its Entities tab.</li>
      </ul>
    </HelpItem>

    <HelpItem title="What is an application?">
      <p>
        An application is the backend of one client app, such as a website or a mobile app. It holds that app's data model
        (entities and properties), its users and how they sign in, security rules, files, email settings, custom endpoints
        and reports.
      </p>
      <p>
        Creating one gives it an application token, which your client app sends with every API call, and the system
        entities Users, AuthTokens and Files (plus the differentiation entity, if you name one).
      </p>
    </HelpItem>

    <HelpItem title="Which server should I pick?">
      <p>
        A server is an Apilane API deployment registered in this Portal. It runs the application and answers the calls of
        your client app.
      </p>
      <p>
        Choose the one closest to your users, or the one meant for the stage of this application, such as test or
        production.
      </p>
    </HelpItem>

    <HelpItem title="Which database type should I pick?">
      <p>
        <strong>SQLite</strong> needs nothing else: the data is a file on the server. It suits small applications and
        trying things out.
      </p>
      <p>
        <strong>SQL Server, MySQL and PostgreSQL</strong> keep the data in a database that you run, so you give Apilane its
        connection string. Apilane creates and changes the tables and columns; backups, scaling and availability of that
        database stay yours.
      </p>
    </HelpItem>

    <HelpItem title="What is the differentiation entity?">
      <p>
        Use it when one application serves several separate groups, such as companies, schools or shops, and each group must
        see only its own records.
      </p>
      <p>
        Name the group, for example <code>Company</code>. Apilane adds a Company entity and a <code>Company_ID</code>
        property to every user, and lets you mark entities as differentiated. A user can then read and write only the
        records of a differentiated entity that carry the same <code>Company_ID</code> as theirs.
      </p>
      <p>
        Your client app decides which <code>Company_ID</code> each user gets, for example with a custom endpoint.
      </p>
    </HelpItem>

    <HelpItem title="What can I change later?">
      <p>
        The name, at any time, and the connection string. The server, the database type and the differentiation entity stay
        as they are for the life of the application, so leave the differentiation entity empty if you do not need it.
      </p>
    </HelpItem>

    <HelpItem title="What comes next?">
      <ol>
        <li>Add entities and their properties on the Entities tab.</li>
        <li>
          Add access rules for them on the Security tab. Until you do, only the people who manage the application (its owner,
          its collaborators and the administrators of the instance), working in the Portal, can reach the data.
        </li>
        <li>
          Call the API from your client app, with the address and token shown under Info on the application's card.
        </li>
      </ol>
    </HelpItem>
  </HelpSheet>
</template>
