<script setup lang="ts">
import HelpItem from '@/components/HelpItem.vue'
import HelpSheet from '@/components/HelpSheet.vue'

// RAISERROR is the way to fail a query on SQL Server only.
defineProps<{ databaseType?: string }>()
</script>

<template>
  <HelpSheet
    title="About the endpoint editor"
    lead="Write the SQL query that the API server runs when this endpoint is called, try it, then give the roles you choose access to it."
  >
    <HelpItem title="What you can do here" open>
      <ol>
        <li>Pick a <strong>name</strong>. The address of the endpoint ends in it.</li>
        <li>Add a <strong>description</strong> to remember why the endpoint exists.</li>
        <li>Write the <strong>SQL</strong> to run on every call.</li>
        <li><strong>Try it</strong> with the Test button of the Result panel, with sample values for the parameters.</li>
        <li><strong>Save</strong>. For an existing endpoint a window first shows what changes (the SQL line by line); Back returns to the editor. Then give access to the roles that may call it on the Security tab.</li>
      </ol>
    </HelpItem>

    <HelpItem title="Writing the query">
      <ul>
        <li>
          Any SQL your database understands: selects, joins and aggregates, but also inserts, updates and deletes. The
          names of your entities and properties are offered as you type. On SQLite, statements that reach outside the
          application's own database, such as ATTACH or most PRAGMA, are refused.
        </li>
        <li>
          Parameters go in braces: <code>{ProductID}</code> becomes a query-string parameter named ProductID. A name has
          letters and underscores only, and the query string must spell it exactly as the SQL does, capitals included.
        </li>
        <li>
          <strong>Parameters can only be long integers.</strong> A value that is not one is replaced by
          <code>null</code>.
        </li>
        <li>
          <code>{Owner}</code> is replaced by the ID of the signed-in user who made the call. For a call without a signed-in
          user it is not replaced, and the database refuses the query.
        </li>
        <li>
          A query with several statements returns its results as a list of lists, one list for each result set.
        </li>
      </ul>
    </HelpItem>

    <HelpItem title="Trying it">
      <p>
        The Result panel runs the SQL as it is in the editor, so you do not need to save first. Give the parameters values
        (whole numbers; a box left empty is sent as <code>null</code>) and click Test to see what the query returns.
      </p>
      <p>
        A test never commits: any change the query makes is rolled back, so you can try an update or a delete safely. A
        call, and so a test, is given 20 seconds; after that its transaction is cancelled and nothing is kept.
      </p>
      <p>
        A test does not fill in <code>{Owner}</code>, so a query that uses it fails there. Put a user ID in its place to try
        it.
      </p>
      <p v-if="databaseType === 'MySQL'">
        On MySQL, a statement that changes tables or columns (CREATE, ALTER, DROP, TRUNCATE, RENAME) is committed at once and
        cannot be undone, not even in a test.
      </p>
    </HelpItem>

    <HelpItem title="What happens on a real call">
      <p>
        The query runs inside a transaction. If it fails, nothing it did is kept; if it succeeds, the changes are committed
        and the result is returned as JSON.
      </p>
    </HelpItem>

    <HelpItem title="Who may call it">
      <p>
        Nobody, until you say so: a custom endpoint is an item in the access rules of the Security tab. A saved endpoint
        with no rule answers every call with a refusal.
      </p>
    </HelpItem>

    <HelpItem title="Renaming">
      <p>
        You can rename an endpoint at any time, but its address changes with it, and the access rules are tied to the name:
        after a rename that changes more than the letter case, the endpoint has no access until you give it again.
      </p>
    </HelpItem>

    <HelpItem title="What not to do">
      <ul>
        <li>
          Do not create or drop tables or columns. The application will no longer match its definition, and the only fix
          is to rebuild it, which deletes all the data.
        </li>
        <li>
          Do not use an endpoint to create, change or delete records that need the checks of the standard calls (required
          values, limits, unique values and foreign keys), unless you check them yourself.
        </li>
        <li>
          Do not use <code>BEGIN</code>, <code>COMMIT</code> or <code>ROLLBACK</code> in the query. It already runs inside a
          transaction, and ending that yourself can break the all-or-nothing behaviour.
        </li>
      </ul>
    </HelpItem>

    <HelpItem v-if="databaseType === 'SQLServer'" title="Raising an error of your own">
      <p>
        On SQL Server, <code>RAISERROR('Your error message', 16, 1);</code> fails the query with a message of your own.
        Because a failed query is not committed, it also undoes what the query did before it.
      </p>
    </HelpItem>
  </HelpSheet>
</template>
