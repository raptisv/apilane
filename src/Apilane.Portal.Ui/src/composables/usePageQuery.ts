import { computed } from 'vue'
import type { ComputedRef } from 'vue'
import { useRoute } from 'vue-router'

/**
 * The page number of a paged list, read from the `?page=` query of the address. Anything that is
 * not a whole number from 1 to 2147483647 (the largest page the API accepts) counts as page 1.
 * Use it with useAsync and AppPagination:
 *
 *   const page = usePageQuery()
 *   const { data, reload } = useAsync(() => unwrap(api.GET('/api/v1/...', { params: { query: { Page: page.value } } })))
 *   watch(page, () => void reload())
 *
 * Reload, not the `watch` option of useAsync: that drops the data, which unmounts the pagination
 * and loses keyboard focus.
 */
export function usePageQuery(): ComputedRef<number> {
  const route = useRoute()

  return computed(() => {
    const value = route.query.page
    const page = Number(Array.isArray(value) ? value[0] : value)

    return Number.isInteger(page) && page >= 1 && page <= 2147483647 ? page : 1
  })
}
