<script setup lang="ts">
import HelpItem from '@/components/HelpItem.vue'
import HelpSheet from '@/components/HelpSheet.vue'
</script>

<template>
  <HelpSheet
    title="About users"
    lead="The people who can sign in to this Portal. This is where you decide who administers the instance."
  >
    <HelpItem title="What you can do here" open>
      <ul>
        <li>See <strong>who has an account</strong>, their role and when they last signed in.</li>
        <li><strong>Make admin</strong> or <strong>Make user</strong> changes someone's role.</li>
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

    <HelpItem title="Where are scripts and AI agents?">
      <p>
        Open <strong>Instance &gt; Agents</strong> to add or remove accounts for scripts and AI agents. They use a key
        instead of signing in and have their own list.
      </p>
    </HelpItem>
  </HelpSheet>
</template>
