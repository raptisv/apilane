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
    <Button v-if="previous >= 1" as-child variant="outline" size="sm">
      <!-- RouterLink ignores the query, so it would mark every link here as the current page. -->
      <RouterLink :to="to(previous)" aria-current-value="false">
        <ChevronLeftIcon />
        <span class="sr-only sm:not-sr-only">Previous</span>
      </RouterLink>
    </Button>
    <Button v-else variant="outline" size="sm" disabled>
      <ChevronLeftIcon />
      <span class="sr-only sm:not-sr-only">Previous</span>
    </Button>

    <Button v-for="p in pages" :key="p" as-child :variant="p === page ? 'default' : 'ghost'" size="sm" class="min-w-7 px-1.5">
      <RouterLink :to="to(p)" :aria-current="p === page ? 'page' : undefined">
        <span class="sr-only">Page</span>
        {{ p }}
      </RouterLink>
    </Button>

    <Button v-if="page < totalPages" as-child variant="outline" size="sm">
      <RouterLink :to="to(page + 1)" aria-current-value="false">
        <span class="sr-only sm:not-sr-only">Next</span>
        <ChevronRightIcon />
      </RouterLink>
    </Button>
    <Button v-else variant="outline" size="sm" disabled>
      <span class="sr-only sm:not-sr-only">Next</span>
      <ChevronRightIcon />
    </Button>
  </nav>
</template>
