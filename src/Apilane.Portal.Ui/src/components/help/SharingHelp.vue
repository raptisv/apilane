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
        <li><strong>Share</strong> with a person by their e-mail address, or <strong>Share with agent</strong> by selecting its name.</li>
        <li>See who it is <strong>shared with</strong>, and write to them.</li>
        <li>Choose an agent's <strong>read, write and deletion rights</strong> for each part of the application, when sharing or later.</li>
        <li><strong>Stop sharing</strong> with anyone, at any time.</li>
      </ul>
    </HelpItem>

    <HelpItem title="What is a collaborator?">
      <p>
        A user you share the application with. They see it in their own list and can manage it: its entities, data,
        security, custom endpoints, e-mail settings and reports.
      </p>
      <ul>
        <li><strong>A person has full access to modify the application, even delete it.</strong> An agent gets only the rights you grant.</li>
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
        An agent is an account for a script or an AI agent, created by an administrator under <strong>Instance &gt; Agents</strong>.
        Choose <strong>Share with agent</strong> and select its name. Only agents that are not yet collaborators are offered.
        After selecting one, you can see its other applications that you also have access to.
      </p>
      <p>
        An agent starts with read-only access, including agents shared before permissions were introduced. Choose its rights
        when sharing, or use <strong>Edit rights</strong> beside an existing agent. Changes apply to its next request.
        Each application has its own rights, and no e-mail is sent to an agent.
      </p>
      <ul>
        <li><strong>None</strong> denies the area; <strong>Read</strong> lets the agent inspect it; <strong>Read and write</strong> also lets it change it.</li>
        <li>Deletion of entities, properties, custom endpoints and reports is a separate choice. It needs read access, but does not need write access.</li>
        <li>Application settings and rebuilding are separate permissions. Rebuilding removes all application data and cannot be undone.</li>
        <li>The application summary stays visible so the agent can identify its shared applications, even when every area is set to None.</li>
        <li>An agent can discover its current rights through the management API, including which operations it may call.</li>
        <li>Sharing, deleting the application, reading its encryption key and instance administration remain for people.</li>
      </ul>
      <p>
        Write access can still have wide effects: security rights replace access rules, entity rights can change constraints,
        and custom endpoint rights can save SQL. Grant only what the agent needs for its work.
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
