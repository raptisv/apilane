<script setup lang="ts">
import HelpItem from '@/components/HelpItem.vue'
import HelpSheet from '@/components/HelpSheet.vue'
</script>

<template>
  <HelpSheet
    title="About cloning"
    lead="Makes a copy of this application on the server and database you choose, with or without its records. Use it for a test copy, a staging copy, or to move an application to another server."
  >
    <HelpItem title="What you can do here" open>
      <ul>
        <li>Choose the <strong>server</strong> the copy runs on, and its database.</li>
        <li>
          Switch <strong>Clone data</strong> on to copy the records too, then tick the entities whose records you want.
        </li>
        <li>Click Clone. A progress page follows the copy while it runs.</li>
      </ul>
    </HelpItem>

    <HelpItem title="What the copy contains">
      <p>
        A copy of the application: the same entities, properties, constraints, custom endpoints, security rules, mail
        settings and other settings, under a new application token and the name followed by <code> - Clone</code>. You own
        the copy. Reports and sharing are not copied. Records are copied only if you switch Clone data on, and only for the
        entities you tick. Uploaded files are never copied.
      </p>
    </HelpItem>

    <HelpItem title="What happens afterwards">
      <p>
        The two applications are independent: a change to the schema or the data of one is not mirrored in the other.
      </p>
      <p>
        Records are copied in the background, and the progress page shows how far it is and the time left. The new
        application is already in your list while it runs, but it works on its API server only when the copy is finished.
      </p>
    </HelpItem>
  </HelpSheet>
</template>
