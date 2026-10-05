<script setup lang="ts">
import HelpItem from '@/components/HelpItem.vue'
import HelpSheet from '@/components/HelpSheet.vue'
</script>

<template>
  <HelpSheet
    title="About reports"
    lead="A report counts or sums the records of an entity, grouped by a property or by date, and shows the result as a table or a chart. Put the ones you look at often on one dashboard."
  >
    <HelpItem title="What you can do here" open>
      <ul>
        <li><strong>New report</strong> opens the editor: title, type of visualization, and one or more series.</li>
        <li>
          <strong>Arrange the dashboard</strong> on a screen at least 768 pixels wide: drag a panel by its title to move it,
          and drag a side or a bottom corner to resize it. The layout is saved as you go. On a narrower screen the panels
          stack in one column.
        </li>
        <li>
          The menu of a panel has <strong>Edit</strong>, <strong>View API endpoint</strong> (the address of the call behind
          each series, to use in your own app; the caller needs read access to the entity) and
          <strong>Delete</strong>. The refresh button reads the data again.
        </li>
      </ul>
    </HelpItem>

    <HelpItem title="Series: what a report shows">
      <p>
        A report has one or more series, and each one is a question about one entity:
      </p>
      <ul>
        <li><strong>Entity</strong>: where the records come from.</li>
        <li>
          <strong>Group by</strong>: the property to split the records by. A date is split by its parts: year, month, day,
          hour, minute or second.
        </li>
        <li>
          <strong>Property</strong>: what to calculate for each group. <code>ID.Count</code> counts the records. A number
          can also be summed, averaged, or have its maximum or minimum taken. In a grid, text and dates can show their
          maximum and minimum.
        </li>
        <li><strong>Filter</strong>: counts only the records that meet every condition.</li>
      </ul>
      <p>
        Several series put their results side by side, so you can compare them in one chart. A pie chart draws the first
        series only.
      </p>
    </HelpItem>

    <HelpItem title="Visualizations">
      <p>
        <strong>Grid</strong> (a table), <strong>pie</strong>, <strong>line</strong>, <strong>bar</strong>,
        <strong>radar</strong> and <strong>stacked bar</strong>. A line chart groups by a number or a date; the other types
        group by a text, number, date or yes/no property. The editor offers only what suits the type you chose.
      </p>
    </HelpItem>

    <HelpItem title="Time range and maximum points">
      <p>
        <strong>Time range</strong> limits the series that are grouped by a date first, to the last hours, days, months or
        years, such as <code>7d</code>. Other series ignore it.
      </p>
      <p>
        <strong>Max points per series</strong> keeps the biggest groups only (top N), so a report grouped by a property with
        many values stays readable.
      </p>
    </HelpItem>

    <HelpItem title="Live data">
      <p>
        A report stores no numbers: every series is a query that the API server runs each time the report is shown. The
        figures are always current, and a report is only as fast as its queries. Deleting a report never touches the data it
        shows.
      </p>
    </HelpItem>
  </HelpSheet>
</template>
