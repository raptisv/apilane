<script setup lang="ts">
import { ChevronLeftIcon, ChevronRightIcon } from '@lucide/vue'
import { computed } from 'vue'
import { useRoute } from 'vue-router'
import { Button } from '@/components/ui/button'
import { usePageQuery } from '@/composables/usePageQuery'

// Page links under a paged list. The page number lives in the address (?page=), so Back, Forward
// and a copied link work; read it in the screen with usePageQuery. Shows nothing for one page.
//
//   <AppPagination :total="data.Total" :page-size="pageSize" label="Audit log pages" />
const props = defineProps<{
  /** The number of items in the whole list (Total of the list answer). */
  total: number
  pageSize: number
  /** Names the navigation for screen readers. */
  label: string
}>()

const route = useRoute()
const page = usePageQuery()

const totalPages = computed(() => Math.max(1, Math.ceil(props.total / props.pageSize)))

// The current page and three on each side.
const pages = computed(() => {
  const list: number[] = []

  for (let p = Math.max(1, page.value - 3); p <= Math.min(totalPages.value, page.value + 3); p++) {
    list.push(p)
  }

  return list
})

// From a page past the end, Previous goes to the last page that exists.
const previous = computed(() => Math.min(page.value - 1, totalPages.value))

function to(p: number) {
  return { query: { ...route.query, page: String(p) } }
}
</script>

<template>
  <nav v-if="totalPages > 1" :aria-label="label" class="mt-4 flex flex-wrap items-center justify-center gap-1">
    <!--
      RouterLink ignores the query, so it would mark every link here as the current page.
      Previous and Next stay the same element at the ends, disabled in place: swapping one for a
      button would drop the keyboard focus to the page when Next reaches the last page. It then
      points at the page on screen, so even Enter on it goes nowhere.
    -->
    <Button as-child variant="outline" size="sm" class="aria-disabled:pointer-events-none aria-disabled:opacity-50">
      <RouterLink
        :to="to(previous >= 1 ? previous : page)"
        aria-current-value="false"
        :aria-disabled="previous >= 1 ? undefined : 'true'"
        :tabindex="previous >= 1 ? undefined : -1"
      >
        <ChevronLeftIcon />
        <span class="sr-only sm:not-sr-only">Previous</span>
      </RouterLink>
    </Button>

    <Button v-for="p in pages" :key="p" as-child :variant="p === page ? 'default' : 'ghost'" size="sm" class="min-w-7 px-1.5">
      <RouterLink :to="to(p)" :aria-current="p === page ? 'page' : undefined">
        <span class="sr-only">Page</span>
        {{ p }}
      </RouterLink>
    </Button>

    <Button as-child variant="outline" size="sm" class="aria-disabled:pointer-events-none aria-disabled:opacity-50">
      <RouterLink
        :to="to(page < totalPages ? page + 1 : page)"
        aria-current-value="false"
        :aria-disabled="page < totalPages ? undefined : 'true'"
        :tabindex="page < totalPages ? undefined : -1"
      >
        <span class="sr-only sm:not-sr-only">Next</span>
        <ChevronRightIcon />
      </RouterLink>
    </Button>
  </nav>
</template>
