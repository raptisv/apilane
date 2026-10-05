<script setup lang="ts">
import HelpItem from '@/components/HelpItem.vue'
import HelpSheet from '@/components/HelpSheet.vue'
</script>

<template>
  <HelpSheet
    title="About sharing"
    lead="Lets other users of this instance manage this application with you. Each of them is a collaborator."
  >
    <HelpItem title="What you can do here" open>
      <ul>
        <li><strong>Share</strong> an application with another user, by their e-mail address.</li>
        <li>See who it is <strong>shared with</strong>, and write to them.</li>
        <li><strong>Stop sharing</strong> with anyone, at any time.</li>
      </ul>
    </HelpItem>

    <HelpItem title="What is a collaborator?">
      <p>
        A user you share the application with. They see it in their own list and can manage it: its entities, data,
        security, custom endpoints, e-mail settings and reports.
      </p>
      <ul>
        <li><strong>They have full access to modify the application, even delete it.</strong></li>
        <li>
          They need an account on this instance with exactly this address, letter case included. Sharing with an address that
          has no account is accepted, and works once that person has an account with it.
        </li>
        <li>They cannot share the application with anybody else.</li>
        <li>You can remove them at any time.</li>
      </ul>
    </HelpItem>

    <HelpItem title="Agents">
      <p>
        An agent is an account for a script or an AI agent, created by an administrator under Users. Type its address, which
        ends in <code>@agent.local</code>, or pick it from the suggestions: only agents that are not yet collaborators are
        offered.
      </p>
      <p>
        An agent is not notified by e-mail, and cannot delete or rebuild the application: those two stay with people.
      </p>
    </HelpItem>

    <HelpItem title="Who gets an e-mail">
      <p>
        When mail is set up on this instance, a person you share with is told by e-mail. An agent is not. The message
        you get after sharing tells you whether the notice was sent: if it was not, tell the person yourself.
      </p>
    </HelpItem>
  </HelpSheet>
</template>
