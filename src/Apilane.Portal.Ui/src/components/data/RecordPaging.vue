<script setup lang="ts">
import { ChevronLeftIcon, ChevronRightIcon, ChevronsLeftIcon, ChevronsRightIcon } from '@lucide/vue'
import { computed } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { Button } from '@/components/ui/button'
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'
import { usePageQuery } from '@/composables/usePageQuery'
import { formatCount } from '@/lib/entities'
import { pageCount, pageSizes } from '@/lib/records'

// The paging bar under the record grid: page size, first / previous /
// 'page x of y' / next / last, and the total. The page (?page=) and the size (?pageSize=) are in the
// address; a new size starts again at page 1.
const props = defineProps<{
  total: number
  pageSize: number
}>()

const route = useRoute()
const router = useRouter()
const page = usePageQuery()

const pages = computed(() => pageCount(props.total, props.pageSize))

// Page 1 and the default size are left out of the address.
function to(p: number) {
  const { page: _page, ...rest } = route.query

  return { query: p === 1 ? rest : { ...rest, page: String(p) } }
}

// First and previous before 'page x of y', next and last after it. A link is disabled where it leads nowhere.
const before = computed(() => [
  { page: 1, enabled: page.value > 1, icon: ChevronsLeftIcon, text: 'First page' },
  { page: Math.min(page.value - 1, pages.value), enabled: page.value > 1, icon: ChevronLeftIcon, text: 'Previous page' },
])
const after = computed(() => [
  { page: page.value + 1, enabled: page.value < pages.value, icon: ChevronRightIcon, text: 'Next page' },
  { page: pages.value, enabled: page.value < pages.value, icon: ChevronsRightIcon, text: 'Last page' },
])

const size = computed({
  get: () => String(props.pageSize),
  set: (value: string) => {
    const { page: _page, pageSize: _pageSize, ...rest } = route.query

    void router.push({ query: value === String(pageSizes[0]) ? rest : { ...rest, pageSize: value } })
  },
})
</script>

<template>
  <div class="mt-3 flex flex-wrap items-center justify-between gap-x-4 gap-y-2 text-sm text-muted-foreground">
    <div class="flex items-center gap-2">
      <label for="record-page-size" class="whitespace-nowrap">Page size</label>
      <Select v-model="size">
        <SelectTrigger id="record-page-size" size="sm" class="w-20">
          <SelectValue />
        </SelectTrigger>
        <SelectContent>
          <SelectItem v-for="option in pageSizes" :key="option" :value="String(option)">{{ option }}</SelectItem>
        </SelectContent>
      </Select>
      <span class="whitespace-nowrap">{{ formatCount(total) }} {{ total === 1 ? 'record' : 'records' }}</span>
    </div>

    <!--
      Also on a page past the end (an old link, records deleted elsewhere), so there is a way back.
      A link that leads nowhere stays the same element, disabled in place: swapping it for a button
      would drop the keyboard focus to the page when Next reaches the last page. It then points at
      the page on screen, so even Enter on it goes nowhere.
    -->
    <nav v-if="pages > 1 || page > 1" aria-label="Record pages" class="flex items-center gap-1">
      <Button
        v-for="link in before"
        :key="link.text"
        as-child
        variant="outline"
        size="icon-sm"
        class="aria-disabled:pointer-events-none aria-disabled:opacity-50"
        :title="link.text"
      >
        <RouterLink
          :to="to(link.enabled ? link.page : page)"
          aria-current-value="false"
          :aria-disabled="link.enabled ? undefined : 'true'"
          :tabindex="link.enabled ? undefined : -1"
        >
          <component :is="link.icon" />
          <span class="sr-only">{{ link.text }}</span>
        </RouterLink>
      </Button>

      <span class="px-2 whitespace-nowrap" aria-live="polite">Page {{ page }} of {{ pages }}</span>

      <Button
        v-for="link in after"
        :key="link.text"
        as-child
        variant="outline"
        size="icon-sm"
        class="aria-disabled:pointer-events-none aria-disabled:opacity-50"
        :title="link.text"
      >
        <RouterLink
          :to="to(link.enabled ? link.page : page)"
          aria-current-value="false"
          :aria-disabled="link.enabled ? undefined : 'true'"
          :tabindex="link.enabled ? undefined : -1"
        >
          <component :is="link.icon" />
          <span class="sr-only">{{ link.text }}</span>
        </RouterLink>
      </Button>
    </nav>
  </div>
</template>
