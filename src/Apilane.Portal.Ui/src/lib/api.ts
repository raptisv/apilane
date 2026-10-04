import createClient from 'openapi-fetch'
import type { components, paths } from './api-types'
import * as toast from './toast'

/** The request and response shapes of the Portal API, by name (generated from openapi/portal-v1.json). */
export type Schemas = components['schemas']

/**
 * The typed client for the Portal's management API. Paths, parameters and response types come
 * from the generated `api-types.ts`, so a call that does not match the API does not compile.
 * Requests go to the page's own origin and carry the login cookie.
 */
export const api = createClient<paths>()

// The API rejects a write without this header. A page on another site cannot send it along with
// the user's cookie, which protects writes against cross-site request forgery.
const writeMethods = ['POST', 'PUT', 'PATCH', 'DELETE']

api.use({
  onRequest({ request }) {
    if (writeMethods.includes(request.method)) {
      request.headers.set('X-Apilane-Portal', '1')
    }

    return request
  },

  // A write that was saved but left something undone (the API server could not be refreshed) says
  // so in a 'Warning' header, as a text for the user. It is shown here, once, for every screen.
  onResponse({ response }) {
    const warning = response.headers.get('Warning')

    if (warning) {
      toast.warning(warning)
    }
  },
})

/**
 * A failed API call: the HTTP status plus the Portal's error body. It is the only error type
 * `unwrap` throws. A call that never reached the Portal has status 0 and code 'NETWORK'.
 */
export class ApiError extends Error {
  readonly status: number
  readonly code: string
  readonly traceId: string | undefined
  /** The resource the error is about, when the API names one. */
  readonly entity: string | undefined
  /** The request property the error is about, when the API names one. */
  readonly property: string | undefined
  /** Per-property problems of a VALIDATION error; empty otherwise. Forms read it through lib/forms.ts. */
  readonly errors: Schemas['ErrorDetail'][]

  constructor(status: number, body?: unknown) {
    // A proxy or a crashed server can answer with HTML or another JSON shape: only the Portal's own body is used.
    const error = isErrorBody(body) ? body : undefined

    super(typeof error?.Message === 'string' ? error.Message : `The request failed (${status}).`)
    this.name = 'ApiError'
    this.status = status
    this.code = error?.Code ?? 'ERROR'
    this.traceId = error?.TraceId ?? undefined
    this.entity = error?.Entity ?? undefined
    this.property = error?.Property ?? undefined
    this.errors = Array.isArray(error?.Errors) ? error.Errors : []
  }
}

function isErrorBody(body: unknown): body is Partial<Schemas['ErrorResponse']> & { Code: string } {
  return typeof body === 'object' && body !== null && typeof (body as { Code?: unknown }).Code === 'string'
}

type ApiResult<T> = { data?: T; error?: unknown; response: Response }

/**
 * Returns the data of a successful call and throws an {@link ApiError} for a failed one, so
 * screens write `await unwrap(api.GET(...))` and handle failures in one place.
 * A 401 means the session ended: the browser goes to the login page and comes back afterwards.
 */
export async function unwrap<T>(call: Promise<ApiResult<T>>): Promise<T> {
  try {
    return await unwrapAnonymous(call)
  } catch (error) {
    if (error instanceof ApiError && error.status === 401) {
      redirectToLogin()
      // The page is navigating away; never resolve, so no screen flashes an error first.
      return new Promise<never>(() => {})
    }

    throw error
  }
}

/**
 * `unwrap` for the calls a visitor makes without a session (sign in, register, password reset):
 * a 401 is thrown like any other failure instead of sending the browser to the login page.
 * A failed sign-in is a 401, and its message belongs in the login form.
 */
export async function unwrapAnonymous<T>(call: Promise<ApiResult<T>>): Promise<T> {
  let result: ApiResult<T>

  try {
    result = await call
  } catch {
    // fetch itself failed (offline, Portal down) or the answer could not be read.
    throw new ApiError(0, { Code: 'NETWORK', Message: 'The Portal could not be reached.' })
  }

  const { data, error, response } = result

  if (!response.ok) {
    throw new ApiError(response.status, error)
  }

  return data as T
}

/** The address of the login page of this UI. `returnUrl` is where to come back to after signing in. */
export function loginUrl(returnUrl?: string): string {
  const login = `${import.meta.env.BASE_URL}account/login`

  return returnUrl ? `${login}?returnUrl=${encodeURIComponent(returnUrl)}` : login
}

/**
 * Leaves for the login page with a full page load, so nothing of the ended session stays in
 * memory. Signing in brings the user back to the address they were on.
 */
export function redirectToLogin(): void {
  location.assign(loginUrl(location.pathname + location.search))
}
