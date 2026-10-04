import { ref, shallowRef, watch } from 'vue'
import type { WatchSource } from 'vue'

/**
 * Runs a loader and exposes the states every screen has to show: loading, failed, loaded.
 * Use it for every read so those states look and behave the same everywhere.
 *
 *   const { data, error, loading, reload } = useAsync(() => unwrap(api.GET('/api/v1/...')))
 *
 * - `loading` is true while there is nothing to show yet: render the skeleton.
 * - `reload()` fetches again and keeps the current data on screen while it runs; `refreshing` is
 *   true meanwhile. If it fails, the data is dropped and `error` is set, so the screen shows
 *   ErrorState. Call it after a write.
 * - `watch` names what the loader depends on, for example a route parameter. When it changes,
 *   the old data is dropped and the loader runs again:
 *
 *   useAsync(() => unwrap(api.GET('/api/v1/things/{id}', ...)), { watch: () => route.params.id })
 */
export function useAsync<T>(load: () => Promise<T>, options: { watch?: WatchSource | WatchSource[] } = {}) {
  const data = shallowRef<T>()
  const error = shallowRef<Error>()
  const loading = ref(true)
  const refreshing = ref(false)

  // Only the latest run may write its result: a slow earlier answer must not replace a newer one.
  let latest = 0

  async function run(keepData: boolean): Promise<void> {
    const id = ++latest

    if (!keepData) {
      data.value = undefined
    }

    loading.value = data.value === undefined
    refreshing.value = !loading.value
    error.value = undefined

    try {
      const result = await load()

      if (id === latest) {
        data.value = result
      }
    } catch (e) {
      if (id === latest) {
        // A failed reload leaves nothing to show but the error, so a retry starts from the skeleton.
        data.value = undefined
        error.value = e instanceof Error ? e : new Error(String(e))
      }
    } finally {
      if (id === latest) {
        loading.value = false
        refreshing.value = false
      }
    }
  }

  function reload(): Promise<void> {
    return run(true)
  }

  if (options.watch) {
    const sources = Array.isArray(options.watch) ? options.watch : [options.watch]
    watch(sources, () => void run(false))
  }

  void run(false)

  return { data, error, loading, refreshing, reload }
}
