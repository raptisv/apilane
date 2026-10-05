<script setup lang="ts">
import HelpItem from '@/components/HelpItem.vue'
import HelpSheet from '@/components/HelpSheet.vue'
</script>

<template>
  <HelpSheet
    title="About instance settings"
    lead="The settings of the Portal itself, as opposed to those of one application: who may register, how the Portal and its API servers trust each other, the mail it sends, and a backup of its database."
  >
    <HelpItem title="What you can do here" open>
      <ul>
        <li>
          <strong>General</strong>: name the instance, allow or stop new users from registering, and change the
          installation key.
        </li>
        <li><strong>Mail</strong>: the mail server the Portal uses to send its own mail, such as the mail for a forgotten password.</li>
        <li><strong>Backup database</strong>: download a copy of the Portal database.</li>
      </ul>
    </HelpItem>

    <HelpItem title="Allow new users to register">
      <p>
        When it is on, anyone who can reach the Portal can create an account. If the instance is on the internet, keep it
        off. To let one person in, switch it on for a while and off again once they have registered.
      </p>
    </HelpItem>

    <HelpItem title="Installation key">
      <p>
        A secret that the Portal and its API servers use to authenticate to each other. Do not share it. It is shown only as
        a hint that one is set. A new key is 30 to 100 characters without spaces.
      </p>
      <p>
        The Portal uses the key stored here, both to check its API servers and to call them. If you change it, set the same
        value as the <code>InstallationKey</code> setting of every API server and restart the API servers. The Portal needs
        no restart. Until the servers run with the new key, new applications cannot be created, and the Portal and the
        servers refuse each other.
      </p>
    </HelpItem>

    <HelpItem title="Mail">
      <p>
        These settings send the Portal's own mail: the mail for a forgotten password, the notice after a password change
        and the notice that an application was shared with someone. They do not serve any application: each application has
        its own mail settings on its Email tab. Without them, people who forget their password cannot reset it.
      </p>
    </HelpItem>

    <HelpItem title="Backup database">
      <p>
        Downloads a copy of the Portal database as <code>Apilane.db</code>: users, applications, settings and the audit log.
        It does not contain the records of your applications, which live in their own databases on the API servers. Every
        download is written to the audit log.
      </p>
      <p>
        <strong>The file contains every secret of the instance</strong>: password hashes, the keys of agents as hashes, the
        stored tokens of signed-in users, the installation key and the mail password of the instance, and the connection
        strings, encryption keys and mail passwords of every application. Store it somewhere safe.
      </p>
    </HelpItem>

    <HelpItem title="Instance name">
      <p>
        3 to 16 characters. It is shown in the Portal, including the sign-in page, and in the subject of the mail that tells
        someone an application was shared with them.
      </p>
    </HelpItem>
  </HelpSheet>
</template>
