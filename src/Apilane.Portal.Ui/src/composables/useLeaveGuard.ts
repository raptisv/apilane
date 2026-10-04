import { onBeforeUnmount, ref } from 'vue'
import { onBeforeRouteLeave, onBeforeRouteUpdate, useRouter } from 'vue-router'
import type { RouteLocationNormalized } from 'vue-router'

/**
 * Stops the user from losing unsaved changes by leaving the screen. Use it on a screen that
 * collects several edits before one Save. UnsavedChangesBar already calls it and shows the
 * question, so a screen with that bar needs nothing else.
 *
 *   const { open: leaveOpen, leave } = useLeaveGuard(() => dirty.value)
 *
 *   <ConfirmDialog v-model:open="leaveOpen" title="Leave without saving?" ... :action="leave" />
 *
 * - A link inside this UI opens the question (`open`); `leave()` then goes where the link led.
 * - Closing the tab, a reload or a link that leaves this UI gets the browser's own question.
 */
export function useLeaveGuard(dirty: () => boolean) {
  const router = useRouter()
  const open = ref(false)
  let target: RouteLocationNormalized | undefined
  let confirmed = false

  function guard(to: RouteLocationNormalized): boolean {
    if (confirmed || !dirty()) {
      return true
    }

    target = to
    open.value = true
    return false
  }

  onBeforeRouteLeave(guard)
  // The same screen of another entity or application is the same route with other parameters:
  // that leaves this screen too. A change of the query or hash alone does not.
  onBeforeRouteUpdate((to, from) => to.path === from.path || guard(to))

  /** Leaves for the screen the user asked for. Returns true, so it can be the action of a ConfirmDialog. */
  async function leave(): Promise<boolean> {
    if (!target) {
      return true
    }

    confirmed = true

    // Left with the browser's Back button: step back instead of pushing a copy of the previous
    // entry. router.back() returns before the navigation, so the guard opens again after it.
    if (router.options.history.state.back === target.fullPath) {
      const stop = router.afterEach(() => {
        stop()
        confirmed = false
      })
      router.back()
      return true
    }

    try {
      await router.push(target.fullPath)
    } finally {
      // If the navigation did not happen after all, the screen is still guarded.
      confirmed = false
    }

    return true
  }

  function beforeUnload(event: BeforeUnloadEvent): void {
    if (dirty()) {
      // This is what makes the browser ask; it shows its own text.
      event.preventDefault()
    }
  }

  window.addEventListener('beforeunload', beforeUnload)
  onBeforeUnmount(() => window.removeEventListener('beforeunload', beforeUnload))

  return { open, leave }
}
