<script setup lang="ts">
import HelpItem from '@/components/HelpItem.vue'
import HelpSheet from '@/components/HelpSheet.vue'
</script>

<template>
  <HelpSheet
    title="About servers"
    lead="A server is an Apilane API deployment that this Portal manages. Every application runs on one server, so people can create applications only once at least one is registered here."
  >
    <HelpItem title="What you can do here" open>
      <ul>
        <li><strong>Add server</strong> registers an API deployment by its name and address.</li>
        <li>
          <strong>Read the list</strong>: the dot tells whether the server answers, its name opens its health check in a new
          tab, and the number is how many applications run on it.
        </li>
        <li><strong>Edit</strong> a server to change its name or its address.</li>
        <li><strong>Delete</strong> a server that has no applications.</li>
      </ul>
    </HelpItem>

    <HelpItem title="What is a server?">
      <p>
        The API server is the part of Apilane that your client apps call. It answers requests for the applications bound to
        it, and the Portal configures it: when you add an entity or change a rule here, the Portal tells the server.
      </p>
      <p>
        You can run one server for everything or several, for example one for tests and one for production, in other places,
        or sized differently.
      </p>
    </HelpItem>

    <HelpItem title="The address">
      <p>
        The base URL of the API server, such as <code>https://api.example.com</code>. The Portal uses it to talk to the
        server, and the Info of each application shows it to the developers who call the API.
      </p>
      <p>
        A wrong address stops every application on that server from working in the Portal, and it is also the address your
        client apps are told to call. Check that it answers before you save it.
      </p>
    </HelpItem>

    <HelpItem title="How the Portal and a server trust each other">
      <p>
        They share the <strong>installation key</strong>, which you set in Settings. A server started with another key cannot
        talk to the Portal, and the Portal cannot call it. After you change the key there, set the same value as the
        <code>InstallationKey</code> setting of every server and restart the servers. The Portal needs no restart.
      </p>
    </HelpItem>

    <HelpItem title="Deleting a server">
      <p>
        A server cannot be deleted while applications run on it. Deleting one only removes it from the Portal: the
        deployment itself is not touched.
      </p>
    </HelpItem>
  </HelpSheet>
</template>
