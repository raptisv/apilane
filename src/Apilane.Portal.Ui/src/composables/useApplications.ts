import { computed } from 'vue'
import { api, unwrap } from '@/lib/api'
import { useAsync } from './useAsync'

/**
 * The applications of the signed-in user (owned and shared). There is ONE load for the whole app:
 * the sidebar switcher and the applications page read the same list, and a `reload()` by either
 * updates both. Call `reload()` after anything that adds, removes or renames an application.
 *
 *   const { applications, error, loading, reload } = useApplications()
 *
 * The first caller starts the load. Signing out is a full page load, so the list never outlives
 * the session it was loaded for.
 */
export function useApplications() {
  shared ??= create()

  return shared
}

let shared: ReturnType<typeof create> | undefined

function create() {
  const { data, error, loading, refreshing, reload } = useAsync(() => unwrap(api.GET('/api/v1/applications')))

  /** Undefined until loaded, and after a failed load. */
  const applications = computed(() => data.value?.Data)

  return { applications, error, loading, refreshing, reload }
}
