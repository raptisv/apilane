<script setup lang="ts">
import HelpItem from '@/components/HelpItem.vue'
import HelpSheet from '@/components/HelpSheet.vue'
</script>

<template>
  <HelpSheet
    title="About security"
    lead="Decides who can use the API of this application: how users sign in and register, which addresses may call it, and what each kind of caller may do with each entity."
  >
    <HelpItem title="What you can do here" open>
      <ul>
        <li>
          <strong>Sign-in, Register and Files</strong> set how long a sign-in lasts, whether people can register
          themselves, and the largest file they can upload.
        </li>
        <li><strong>IP access</strong> allows or blocks callers by their address.</li>
        <li><strong>Forgot password</strong> gives you the page and the endpoint that start a password reset.</li>
        <li>
          <strong>Access rules</strong> decide, entity by entity and endpoint by endpoint, who may read, create, change or
          delete. Their own Help explains them.
        </li>
      </ul>
      <p>The settings above and the access rules below are saved separately, each with its own Save.</p>
    </HelpItem>

    <HelpItem title="Sign-in">
      <ul>
        <li>
          <strong>Auth token lifetime</strong>: how long a sign-in stays valid. It is renewed while the user keeps using
          the application, so a user who is inactive for about this long must sign in again.
        </li>
        <li><strong>Only one sign-in at a time</strong>: a new sign-in ends the user's other sessions.</li>
        <li>
          <strong>Unconfirmed e-mail</strong>: whether a user who has not yet confirmed their address can sign in. The
          confirmation mail needs the settings on the Email tab.
        </li>
      </ul>
    </HelpItem>

    <HelpItem title="Register">
      <p>
        Switched on, anyone who can reach the API can create a user account. Switched off, nobody can, not even you with
        Register user in the data browser: the call fails with "Register is not allowed". Switch it on while accounts are
        created, and off again.
      </p>
    </HelpItem>

    <HelpItem title="Files">
      <p>
        The largest file a user can upload, from 1 KB to 25600 KB (25 MB). Whether a user can upload at all, and whose
        files they can read, is an access rule on the Files entity.
      </p>
    </HelpItem>

    <HelpItem title="IP access">
      <p>
        A list of IPv4 addresses, compared exactly with the address of the caller. Choose what the list means:
      </p>
      <ul>
        <li><strong>Blocked</strong>: these addresses are refused, every other one is accepted.</li>
        <li><strong>Allowed</strong>: only these addresses are accepted, every other one is refused.</li>
      </ul>
      <p>
        An empty list accepts everyone. The list does not apply to the people who manage the application (its owner, its
        collaborators and the administrators of the instance) when they work in the Portal, so a mistake here cannot lock
        you out.
      </p>
    </HelpItem>

    <HelpItem title="Forgot password">
      <p>
        Two ways to start a reset, both of which send the user an e-mail with a link. The page is ready-made: send users to
        it from your sign-in screen. The endpoint is for your own screen: call it with the user's address. Both need the
        mail server on the Email tab.
      </p>
    </HelpItem>

    <HelpItem title="Where do roles come from?">
      <p>
        A role is a name in the <code>Roles</code> property of a user: lowercase letters, separated by commas. Add it by
        editing the user in the data browser, and it shows up in the access rules after a reload. There is no separate list
        of roles to maintain.
      </p>
    </HelpItem>
  </HelpSheet>
</template>
