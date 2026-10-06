<script setup lang="ts">
import HelpItem from '@/components/HelpItem.vue'
import HelpSheet from '@/components/HelpSheet.vue'
</script>

<template>
  <HelpSheet
    title="About users"
    lead="Everyone who can sign in to this Portal, and its agents: accounts for scripts and AI agents. This is where you decide who administers the instance."
  >
    <HelpItem title="What you can do here" open>
      <ul>
        <li>See <strong>who has an account</strong>, their role and when they last signed in.</li>
        <li><strong>Make admin</strong> or <strong>Make user</strong> changes someone's role.</li>
        <li><strong>Add agent</strong> creates an account for a script or an AI agent, with a key.</li>
        <li><strong>Delete</strong> an agent to take its key away.</li>
      </ul>
    </HelpItem>

    <HelpItem title="Admin and User">
      <p>
        A <strong>User</strong> works with the applications they own or that are shared with them. An
        <strong>Admin</strong> also manages the instance: its servers, users, settings and audit log. An Admin sees every
        application and can browse its records, but needs it shared to change its entities, security or settings.
      </p>
      <p>
        You cannot change your own role. A role change takes effect within 30 minutes, or at once when the person signs in
        again, so with several administrators do not demote each other at the same time.
      </p>
    </HelpItem>

    <HelpItem title="How do people get an account?">
      <p>
        They register on the sign-in page, if <strong>Allow new users to register</strong> is on in Settings. Switch it on
        for as long as it takes someone to register, and off again. There is no way to create or remove a person's account
        from this page.
      </p>
    </HelpItem>

    <HelpItem title="What is an agent?">
      <p>
        An account without a password, for a script or an AI agent that has to manage applications through the Portal API.
        Its address is the name you give it followed by <code>@agent.local</code>.
      </p>
      <ul>
        <li>
          Right after you add it, you see its <strong>key</strong>, once. The Portal keeps only a hash of it, so copy it now:
          if it is lost, delete the agent and add it again.
        </li>
        <li>
          The agent sends the key with every request, as <code>Authorization: Bearer &lt;key&gt;</code>. It never signs in:
          the key alone is checked on every request.
        </li>
        <li>
          It manages the applications <strong>shared with its address</strong>: share an application with it, on the Sharing
          tab of the application. Agents start read-only; the owner chooses their rights when sharing or with Edit rights later.
        </li>
        <li>
          It can never be an administrator or delete an application. Rebuilding and deletion within selected areas require
          separate permission from the application owner.
        </li>
        <li>
          Deleting an agent stops its key at once and removes it from every application shared with it.
        </li>
      </ul>
    </HelpItem>
  </HelpSheet>
</template>
