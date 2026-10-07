<script setup lang="ts">
import HelpItem from '@/components/HelpItem.vue'
import HelpSheet from '@/components/HelpSheet.vue'
</script>

<template>
  <HelpSheet
    title="About agents"
    lead="Accounts for scripts and AI agents that manage applications through the Portal API. They use a key instead of a password and can work only with applications shared with them."
  >
    <HelpItem title="What you can do here" open>
      <ul>
        <li><strong>Add agent</strong> creates an account and shows its key once.</li>
        <li>See each agent's name and when it was <strong>Added</strong>.</li>
        <li><strong>Delete</strong> an agent to revoke its key and remove all its application access.</li>
      </ul>
    </HelpItem>

    <HelpItem title="The name and key">
      <p>
        Choose a name when adding an agent. Then copy its <strong>key</strong> and give it to the script or AI agent. The key
        is shown only then: the Portal keeps a hash of it and cannot show it again. If it is lost, delete the agent and add
        it again.
      </p>
      <p>
        The agent sends the key with every request, as <code>Authorization: Bearer &lt;key&gt;</code>. It never signs in:
        the key is checked on every request. The Added date is when the account was created, not its last activity.
      </p>
    </HelpItem>

    <HelpItem title="Give an agent access to an application">
      <p>
        On the application's <strong>Sharing</strong> tab, select the agent by name. Agents start read-only; the
        application owner chooses their rights when sharing or with <strong>Edit rights</strong> later. Choose only the
        areas the agent needs to read, change or delete.
      </p>
      <p>
        An agent can never be an administrator or delete an application. Rebuilding and deletion within selected areas
        require separate permission from the application owner.
      </p>
      <p>
        Renaming entities or properties and changing mail settings always require a person, regardless of the agent's
        rights. Email settings offer read access only. Renaming a custom endpoint follows its existing permissions.
      </p>
      <p>
        Older permission to change email settings no longer applies. The agent keeps its other permissions.
      </p>
    </HelpItem>

    <HelpItem title="Revoke access">
      <p>
        <strong>Delete</strong> stops the agent's key at once and removes it from every application shared with it. You must
        type its name to confirm. This cannot be undone.
      </p>
      <p>
        To remove access to just one application, remove the agent from that application's <strong>Sharing</strong> tab
        instead. Its key and access to other shared applications stay in place.
      </p>
    </HelpItem>
  </HelpSheet>
</template>
