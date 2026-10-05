<script setup lang="ts">
import HelpItem from '@/components/HelpItem.vue'
import HelpSheet from '@/components/HelpSheet.vue'
</script>

<template>
  <HelpSheet
    title="About all applications"
    lead="Every application of this instance, whoever owns it. As an administrator you can look into any of them, to support a user or to see what the instance is hosting."
  >
    <HelpItem title="What you can do here" open>
      <ul>
        <li><strong>Search</strong> by name, token or owner's e-mail address.</li>
        <li>
          <strong>Open the details</strong> of an application with the arrow at the end of its row: its ID, whether a
          connection string and a mail password are stored, its maximum file size, token lifetime and mail settings. Name,
          token, owner, server, database and status are in the row.
        </li>
        <li>
          The row menu opens the <strong>data browser</strong> of any application, and the
          <strong>application itself</strong> if it is one of yours or is shared with you.
        </li>
        <li><strong>Clear cache</strong> makes the API server read the application again.</li>
      </ul>
    </HelpItem>

    <HelpItem title="What an administrator can and cannot do">
      <p>
        You can browse and change the records of any application, but you cannot open its entities, security rules or other
        settings unless it is shared with you. To help with those, ask its owner to share it with you.
      </p>
    </HelpItem>

    <HelpItem title="When to clear the cache">
      <p>
        An API server keeps the definition of each application in memory, and the Portal refreshes it whenever you save a
        change. Clear the cache if a server seems to answer with an old version of an application. It does no harm: the server
        simply reads the application again on its next call.
      </p>
    </HelpItem>
  </HelpSheet>
</template>
