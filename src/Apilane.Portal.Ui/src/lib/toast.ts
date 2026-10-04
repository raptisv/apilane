import { toast } from 'vue-sonner'
import { ApiError } from './api'

/**
 * Short messages in the corner of the screen. Use them for the outcome of an action that has no
 * other place to show it: `success` after a write, `error` when an action outside a form fails.
 * A form shows its own errors (lib/forms.ts); a screen that cannot load shows ErrorState.
 *
 *   import * as toast from '@/lib/toast'
 *
 *   toast.success('Server added.')
 *   toast.error(remove.error.value)
 */
export function success(message: string): void {
  toast.success(message)
}

/**
 * The action succeeded, but something after it did not. Screens rarely call it: the API client
 * (lib/api.ts) shows the 'Warning' header of any answer this way.
 */
export function warning(message: string): void {
  toast.warning(message, { duration: 10000 })
}

/** Shows what went wrong, with the trace id that finds the request in the Portal logs. */
export function error(error: unknown): void {
  const message = error instanceof Error ? error.message : String(error)
  const traceId = error instanceof ApiError ? error.traceId : undefined

  // Stays longer than a success message: there is a trace id to read.
  toast.error(message, { description: traceId ? `Trace ${traceId}` : undefined, duration: 10000 })
}
