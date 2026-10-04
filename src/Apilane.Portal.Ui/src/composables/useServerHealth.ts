import { onMounted, onUnmounted, ref, watch } from 'vue'

const intervalMs = 5000
const timeoutMs = 4000

/** The liveness address of an API server. A trailing slash on the server address is ignored. */
export function healthUrl(serverUrl: string): string {
  return `${serverUrl.replace(/\/+$/, '')}/Health/Liveness`
}

/**
 * Asks an API server whether it is up, straight from the browser: at once, then every 5 seconds.
 * It pauses while the tab is hidden and stops when the component goes away.
 * `online` is undefined until the first answer.
 *
 *   const { online } = useServerHealth(() => props.serverUrl)
 */
export function useServerHealth(serverUrl: () => string) {
  const online = ref<boolean>()

  let timer: ReturnType<typeof setInterval> | undefined
  // Only the latest check may write its result; stop() also bumps it so a late answer is dropped.
  let latest = 0

  async function check(): Promise<void> {
    const id = ++latest
    let ok: boolean

    try {
      // Another origin: no cookie is sent, and a server that does not allow this origin counts as offline.
      const response = await fetch(healthUrl(serverUrl()), {
        cache: 'no-store',
        signal: AbortSignal.timeout(timeoutMs),
      })
      ok = response.ok
    } catch {
      ok = false
    }

    if (id === latest) {
      online.value = ok
    }
  }

  function stop(): void {
    clearInterval(timer)
    timer = undefined
    latest++
  }

  function start(): void {
    stop()
    void check()
    timer = setInterval(() => void check(), intervalMs)
  }

  function onVisibilityChange(): void {
    if (document.hidden) {
      stop()
    } else {
      start()
    }
  }

  onMounted(() => {
    document.addEventListener('visibilitychange', onVisibilityChange)
    onVisibilityChange()
  })

  onUnmounted(() => {
    document.removeEventListener('visibilitychange', onVisibilityChange)
    stop()
  })

  // The address was edited: the old answer says nothing about the new one.
  watch(serverUrl, () => {
    online.value = undefined
    onVisibilityChange()
  })

  return { online }
}
