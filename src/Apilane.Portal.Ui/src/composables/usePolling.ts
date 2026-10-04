import { onMounted, onUnmounted, ref, shallowRef } from 'vue'

/**
 * Asks for the state of something that runs on the server (a clone) at once and then again
 * `intervalMs` after every answer, until `done` says the answer is final. Use it on the screen
 * that follows a long-running operation:
 *
 *   const { data, error, loading, restart } = usePolling(() => unwrap(api.GET('/api/v1/...')), {
 *     intervalMs: 2000,
 *     done: (operation) => operation.Status === 'Completed' || operation.Status === 'Failed',
 *     giveUp: (error) => error instanceof ApiError && error.status === 404,
 *   })
 *
 * - `data` is the last answer. A failed attempt keeps it and is tried again after the interval.
 * - `error` is the failure of the last attempt; the next answer clears it. With `data` set it
 *   means 'not answering right now'; without, there is nothing to show yet (ErrorState, with
 *   `restart` as its retry).
 * - `giveUp` names the failures that will not go away (the operation no longer exists): polling
 *   stops and `error` stays.
 * - `loading` is true until the first answer or failure.
 *
 * It pauses while the tab is hidden, asks at once when the tab is shown again, and stops when
 * the component goes away.
 */
export function usePolling<T>(
  load: () => Promise<T>,
  options: { intervalMs: number; done: (data: T) => boolean; giveUp?: (error: Error) => boolean },
) {
  const data = shallowRef<T>()
  const error = shallowRef<Error>()
  const loading = ref(true)

  let timer: ReturnType<typeof setTimeout> | undefined
  // Only the latest attempt may write its result; stop() also bumps it so a late answer is dropped.
  let latest = 0
  // The answer was final, or the failure one to give up on: nothing more to ask.
  let ended = false

  async function poll(): Promise<void> {
    const id = ++latest

    try {
      const result = await load()

      if (id !== latest) {
        return
      }

      data.value = result
      error.value = undefined
      ended = options.done(result)
    } catch (e) {
      if (id !== latest) {
        return
      }

      error.value = e instanceof Error ? e : new Error(String(e))
      ended = options.giveUp?.(error.value) ?? false
    }

    loading.value = false

    // The next attempt is timed from this answer, so a slow Portal never has two requests waiting.
    if (!ended) {
      timer = setTimeout(() => void poll(), options.intervalMs)
    }
  }

  function stop(): void {
    clearTimeout(timer)
    timer = undefined
    latest++
  }

  function onVisibilityChange(): void {
    stop()

    if (!document.hidden && !ended) {
      void poll()
    }
  }

  /** Starts over after the user asked to try again: the skeleton shows until the next answer. */
  function restart(): void {
    ended = false
    error.value = undefined
    loading.value = data.value === undefined
    onVisibilityChange()
  }

  onMounted(() => {
    document.addEventListener('visibilitychange', onVisibilityChange)
    onVisibilityChange()
  })

  onUnmounted(() => {
    document.removeEventListener('visibilitychange', onVisibilityChange)
    stop()
  })

  return { data, error, loading, restart }
}
