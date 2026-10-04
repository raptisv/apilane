import { ref, shallowRef } from 'vue'

/**
 * Runs a write (create, update, delete) and exposes its state. Use it for every write, next to
 * useAsync for reads. `run` never throws: it returns whether the write succeeded, and `error`
 * holds the failure, for a form (lib/forms.ts) or a toast (lib/toast.ts).
 *
 *   const save = useMutation((body: Schemas['...']) => unwrap(api.POST('/api/v1/...', { body })))
 *
 *   if (await save.run(form)) {
 *     await reload()
 *   }
 */
export function useMutation<Args extends unknown[]>(action: (...args: Args) => Promise<unknown>) {
  const pending = ref(false)
  const error = shallowRef<Error>()

  async function run(...args: Args): Promise<boolean> {
    // A second click while the first write is still running does nothing.
    if (pending.value) {
      return false
    }

    pending.value = true
    error.value = undefined

    try {
      await action(...args)
      return true
    } catch (e) {
      error.value = e instanceof Error ? e : new Error(String(e))
      return false
    } finally {
      pending.value = false
    }
  }

  /** Forgets the last failure, for example when a dialog is opened again. */
  function reset(): void {
    error.value = undefined
  }

  return { run, pending, error, reset }
}
