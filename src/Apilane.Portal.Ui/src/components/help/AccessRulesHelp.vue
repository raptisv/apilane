<script setup lang="ts">
import HelpItem from '@/components/HelpItem.vue'
import HelpSheet from '@/components/HelpSheet.vue'
</script>

<template>
  <HelpSheet
    title="About access rules"
    size="sm"
    lead="An access rule says that one kind of caller may do one thing with one entity or custom endpoint. The API refuses every call that no rule allows."
  >
    <HelpItem title="What you can do here" open>
      <ul>
        <li>
          Pick an <strong>item</strong>: an entity, a custom endpoint, or the application schema (the call that reads the
          definition of the application).
        </li>
        <li>
          Switch a <strong>role</strong> on for an <strong>action</strong>: get (read), post (create), put (change) or
          delete. Each switch is one rule.
        </li>
        <li>Open <strong>Details</strong> under a switch to narrow the rule: which records, which properties, how often.</li>
        <li>
          Click <strong>Save</strong>. Nothing changes for your users until you do. <strong>Tree</strong> and
          <strong>Matrix</strong> show the rules you have saved, as a whole.
        </li>
      </ul>
    </HelpItem>

    <HelpItem title="Who are Anonymous, Authenticated and the roles?">
      <ul>
        <li><strong>Anonymous</strong>: any call without an auth token, or with one that has expired.</li>
        <li><strong>Authenticated</strong>: any call with a valid auth token, whatever roles the user has.</li>
        <li>
          <strong>A role</strong>: signed-in users who have it. Roles are the names in the <code>Roles</code> property of
          your users: lowercase letters, separated by commas. A role appears here once a user has it, or once a rule names it.
        </li>
      </ul>
    </HelpItem>

    <HelpItem title="How rules add up">
      <ul>
        <li>Without a rule there is no access.</li>
        <li>
          A call without a valid auth token gets what Anonymous has. A signed-in user gets what Anonymous, Authenticated and
          each of their roles have, together: a role inherits what Authenticated has.
        </li>
        <li>
          Another rule never takes an action away: if one rule allows it, the user can do it. Only
          <strong>Owned records only</strong> narrows what the other rules give.
        </li>
      </ul>
    </HelpItem>

    <HelpItem title="Which records: owned records only">
      <p>
        By default a rule covers all the records of the entity. Choose <strong>Owned records only</strong> and the user
        gets only the records they created (their <code>Owner</code>). It has no effect on Anonymous callers, who have no
        owner, nor on post, which creates a record.
      </p>
      <p>
        Here the narrowest rule wins: if any rule that applies to the user is limited to owned records, the user gets only
        their own records, even when another rule gives all records.
      </p>
    </HelpItem>

    <HelpItem title="Which properties">
      <p>
        On get, post and put you choose which properties the rule covers. The properties of all the rules that apply to a
        user are added together. The <code>ID</code> is always included.
      </p>
      <p>
        A rule with no properties still returns the ID on a get, and refuses a post or a put, so a rule you create without
        choosing properties does not expose anything by accident.
      </p>
    </HelpItem>

    <HelpItem title="Rate limits">
      <p>
        A limit of calls per second, minute or hour. It counts per signed-in user, while all the calls without an auth token
        share one count. Only the rules that apply to the caller are looked at: the most generous limit among them wins, and
        there is no limit at all if one of them has none. People who manage the application, working in the Portal, are
        never limited.
      </p>
    </HelpItem>

    <HelpItem title="Check what a role can really do">
      <p>
        <strong>Matrix</strong> is a table of items against roles. <strong>Tree</strong> lists each item with the roles
        that have access and the actions they may call. Both show what the API server applies once the rules have added up,
        so a role includes what it inherits from Anonymous and Authenticated. They show the saved rules only: save your
        changes first. Use them to see what you have really given away.
      </p>
    </HelpItem>

    <HelpItem title="Rules follow names">
      <p>
        A rule names its entity or custom endpoint. If you rename one, its rules do not follow: give them again under the
        new name.
      </p>
    </HelpItem>
  </HelpSheet>
</template>
