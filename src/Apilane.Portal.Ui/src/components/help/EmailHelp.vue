<script setup lang="ts">
import HelpItem from '@/components/HelpItem.vue'
import HelpSheet from '@/components/HelpSheet.vue'
</script>

<template>
  <HelpSheet
    title="About email"
    lead="Sets up how this application sends mail to its users, and what the mail says. Without a mail server, the application cannot confirm addresses or reset passwords."
  >
    <HelpItem title="What you can do here" open>
      <ul>
        <li>
          Fill in the <strong>SMTP settings</strong>: the mail server this application sends its mail with.
        </li>
        <li>
          Choose the <strong>landing page</strong> where a user arrives after confirming their address.
        </li>
        <li>
          Edit the <strong>email templates</strong>: switch each email on or off and change its subject and its HTML body.
        </li>
      </ul>
    </HelpItem>

    <HelpItem title="SMTP settings">
      <p>
        All six fields are needed: mail server, port, sender address, sender display name, user name and password. Until they
        are filled in, the page says the application cannot send mail. Use the details your mail provider gives you for
        sending through SMTP.
      </p>
      <p>
        The password is never shown again once saved. Leave its box alone to keep it, or type a new one to replace it. Mail
        is sent over SSL/TLS, and a failed send is not reported, so test with a real address.
      </p>
    </HelpItem>

    <HelpItem title="What mail does an application send?">
      <ul>
        <li>
          <strong>Email confirmation</strong>: sent when a user registers, and again when the user asks for a new one, with a
          link to confirm the address. Whether a user must confirm before signing in is set on the Security tab.
        </li>
        <li>
          <strong>Reset password</strong>: sent when a user asks to reset a forgotten password, with a link to choose a new
          one.
        </li>
      </ul>
      <p>
        Each is an email template, and each can be switched off. A mail that is switched off is never sent.
      </p>
    </HelpItem>

    <HelpItem title="Email templates">
      <p>
        Open a template to change its subject and its body, which is HTML. A preview shows the result as you type. The
        <strong>placeholders</strong> listed under the editor are filled in when the mail is sent: the user's id, user name
        and e-mail address, and the link the user has to follow (<code>{confirmation_url}</code> or
        <code>{reset_password_url}</code>). The preview shows them as they are written, and does not run scripts.
      </p>
      <p>
        Templates are kept on the API server of this application, not in the Portal.
      </p>
    </HelpItem>

    <HelpItem title="Email confirmation landing page">
      <p>
        After a user clicks the link in the confirmation mail, they are sent to the address you give here, usually a page of
        your own client app. Leave it empty to use a plain default page. It applies only if the Email confirmation template
        is switched on.
      </p>
    </HelpItem>

    <HelpItem title="Not the same as the instance mail settings">
      <p>
        The mail server of the instance, under Settings in the administration, is used only to send the Portal's own mail,
        such as a forgotten Portal password. It does not serve any application: each application has its own settings here.
      </p>
    </HelpItem>
  </HelpSheet>
</template>
