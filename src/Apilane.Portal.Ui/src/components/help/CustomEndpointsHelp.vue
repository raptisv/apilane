<script setup lang="ts">
import HelpItem from '@/components/HelpItem.vue'
import HelpSheet from '@/components/HelpSheet.vue'
</script>

<template>
  <HelpSheet
    title="About custom endpoints"
    lead="A custom endpoint is a SQL query of your own that the API server runs when a client calls its address. Use one when the standard calls cannot give you what you need, such as a join of several entities or a calculation of your own."
  >
    <HelpItem title="What you can do here" open>
      <ul>
        <li><strong>New endpoint</strong> writes a new query.</li>
        <li>
          The <strong>green button</strong> on a row calls the endpoint in a new tab, as an anonymous caller, so it works
          only if an access rule lets Anonymous call it. The address it opens has no values for the parameters, so they are
          sent as null. To run the SQL as the owner of the application, use Test in the editor: a test does not check the
          access rules.
        </li>
        <li>The <strong>pencil</strong> opens the editor, the <strong>bin</strong> deletes the endpoint.</li>
        <li>The <strong>search</strong> looks through the names, descriptions and SQL of all endpoints.</li>
      </ul>
    </HelpItem>

    <HelpItem title="How a client calls one">
      <p>
        With a GET to <code>/api/Custom/{Name}</code> on the API server, sending the application token in the
        <code>x-application-token</code> header (or as a query-string value), and the user's auth token as
        <code>Authorization: Bearer ...</code> when the caller is signed in. The parameters go in the query string.
      </p>
    </HelpItem>

    <HelpItem title="Who may call it">
      <p>
        Nobody, until you say so. A custom endpoint is an item like any entity: give it access on the Security tab, for the
        roles that should call it. A new endpoint therefore stays private until you do, even though it is saved and listed.
      </p>
    </HelpItem>

    <HelpItem title="Why use one">
      <ul>
        <li>Join entities, and return exactly the columns you want.</li>
        <li>Calculate what the standard Aggregate call cannot, such as a ratio between two entities.</li>
        <li>
          Change records with SQL of your own, for example an update that uses the current values, in one call that either
          succeeds as a whole or does not happen.
        </li>
      </ul>
    </HelpItem>

    <HelpItem title="Keep in mind">
      <p>
        The query runs with full access to the database of the application, whoever calls it, so the access rules on the
        entities do not apply inside it. Decide carefully who may call it, and let it use <code>{Owner}</code> to limit what
        a signed-in user can reach.
      </p>
    </HelpItem>
  </HelpSheet>
</template>
