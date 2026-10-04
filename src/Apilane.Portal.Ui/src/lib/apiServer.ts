import { api, ApiError, unwrap } from './api'

type Query = Record<string, string | number | boolean | undefined>

/**
 * The client for calls that go straight from the browser to an API server (the data plane):
 * storage used, export, e-mail templates, the SQL test of a custom endpoint, records, files and
 * history, and the statistics of a report. Everything about the Portal itself (applications, servers, users,
 * settings) goes through `api` in lib/api.ts instead.
 *
 *   const used = await apiServer(app.Server.ServerUrl, app.Token).get<number>('/api/Application/GetStorageUsed')
 *   const zip = await apiServer(app.Server.ServerUrl, app.Token).getBlob('/api/Application/Export')
 *   await apiServer(app.Server.ServerUrl, app.Token).put('/api/Email/Update', template)
 *   const rows = await apiServer(app.Server.ServerUrl, app.Token).post<Rows>('/api/Custom/TestQuery', body, { OrderId: 5 })
 *   await apiServer(app.Server.ServerUrl, app.Token).delete('/api/Data/Delete', { entity: 'Orders', ids: '4,5' })
 *   const id = await apiServer(app.Server.ServerUrl, app.Token).postFile<number>('/api/Files/Post', 'FileUpload', file)
 *
 * Every call carries the signed-in user's API token, which the Portal hands out at
 * GET /api/v1/session/api-token. A failed call throws the same ApiError as the Portal client, so
 * useAsync, useMutation, ErrorState and the toasts work unchanged.
 */
export function apiServer(serverUrl: string, appToken: string) {
  const base = serverBase(serverUrl)

  async function send(method: Method, path: string, query?: Query, body?: Body, signal?: AbortSignal): Promise<Response> {
    const url = base + path + queryString(query)
    const init = { method, body, signal }

    let used = apiToken()
    let response = await request(url, appToken, await used, init)

    if (response.status === 401) {
      // The token changes at every sign-in, so the one in memory may be old: ask the Portal for
      // it once more. If the session itself has ended, that call leads to the login page (see
      // unwrap). If the session is fine and the API server still refuses, the 401 is thrown like
      // any other failure: sending a signed-in user to the login page would only bounce them back.
      if (token === used) {
        token = undefined
      }

      used = apiToken()
      response = await request(url, appToken, await used, init)
    }

    if (!response.ok) {
      throw new ApiError(response.status, errorBody(await readJson(response)))
    }

    return response
  }

  return {
    /**
     * A GET that answers JSON. `query` values are URL-encoded; a value that is undefined is left
     * out. `signal` cancels it (an AbortController's), for a read that a newer one replaces: the
     * call then rejects with the browser's AbortError, which isAbort() recognises.
     */
    async get<T>(path: string, query?: Query, options: { signal?: AbortSignal } = {}): Promise<T> {
      return answer<T>(await send('GET', path, query, undefined, options.signal))
    },

    /** A GET that answers a file. Save it with saveBlob (lib/download.ts). */
    async getBlob(path: string, query?: Query): Promise<Blob> {
      const response = await send('GET', path, query)

      return response.blob()
    },

    /** A PUT of `body` as JSON that answers JSON. Run it through useMutation like any write. */
    async put<T>(path: string, body: unknown, query?: Query): Promise<T> {
      return answer<T>(await send('PUT', path, query, JSON.stringify(body)))
    },

    /** A POST of `body` as JSON that answers JSON. Run it through useMutation like any write. */
    async post<T>(path: string, body: unknown, query?: Query): Promise<T> {
      return answer<T>(await send('POST', path, query, JSON.stringify(body)))
    },

    /** A DELETE that answers JSON. Run it through useMutation like any write. */
    async delete<T>(path: string, query?: Query): Promise<T> {
      return answer<T>(await send('DELETE', path, query))
    },

    /**
     * A POST of one file as multipart/form-data, in the form field `field`, that answers JSON.
     * The browser sets the Content-Type with its boundary.
     */
    async postFile<T>(path: string, field: string, file: File, query?: Query): Promise<T> {
      const form = new FormData()
      form.append(field, file, file.name)

      return answer<T>(await send('POST', path, query, form))
    },
  }
}

type Method = 'GET' | 'PUT' | 'POST' | 'DELETE'

// JSON text, or the form of a file upload. Both can be sent twice (the retry after a 401).
type Body = string | FormData

/** Whether a call failed only because its AbortSignal cancelled it: nothing to show the user. */
export function isAbort(error: unknown): boolean {
  return error instanceof DOMException && error.name === 'AbortError'
}

async function answer<T>(response: Response): Promise<T> {
  try {
    return (await response.json()) as T
  } catch {
    // A 200 that is not JSON (a proxy's HTML page) or a body that broke off while being read.
    throw new ApiError(response.status, { Code: 'ERROR', Message: 'The API server sent an answer that could not be read.' })
  }
}

/** The address of an API server without a trailing slash, ready to have a path added. */
export function serverBase(serverUrl: string): string {
  return serverUrl.replace(/\/+$/, '')
}

/** The Swagger page of one application on its API server. Open it in a new tab. */
export function swaggerUrl(serverUrl: string, appToken: string, appName: string): string {
  return `${serverBase(serverUrl)}/swagger/index.html${queryString({ appToken, appName })}`
}

// The user's API token. Kept in memory only, never in storage: it is a secret, and it is gone
// when the page is closed or reloaded. Fetched at the first call and shared by all of them.
let token: Promise<string> | undefined

function apiToken(): Promise<string> {
  if (!token) {
    const loading = unwrap(api.GET('/api/v1/session/api-token')).then((body) => body.Token)

    token = loading
    // A failed load must not be remembered, or every later call would fail the same way.
    loading.catch(() => {
      if (token === loading) {
        token = undefined
      }
    })
  }

  return token
}

async function request(
  url: string,
  appToken: string,
  bearer: string,
  init: { method: Method; body: Body | undefined; signal: AbortSignal | undefined },
): Promise<Response> {
  const headers: Record<string, string> = {
    Authorization: `Bearer ${bearer}`,
    'x-application-token': appToken,
    'x-client-id': 'portal',
  }

  if (typeof init.body === 'string') {
    headers['Content-Type'] = 'application/json; charset=utf-8'
  }

  try {
    // Another origin: the login cookie is not sent, only these headers. The X-Apilane-Portal header
    // of lib/api.ts is not needed: it protects the Portal's cookie, and an API server takes none.
    return await fetch(url, { method: init.method, body: init.body, cache: 'no-store', headers, signal: init.signal })
  } catch (error) {
    if (isAbort(error)) {
      throw error
    }

    throw new ApiError(0, { Code: 'NETWORK', Message: 'The API server could not be reached.' })
  }
}

function queryString(query: Query | undefined): string {
  const pairs = Object.entries(query ?? {})
    .filter((pair): pair is [string, string | number | boolean] => pair[1] !== undefined)
    .map(([name, value]) => `${encodeURIComponent(name)}=${encodeURIComponent(value)}`)

  return pairs.length > 0 ? `?${pairs.join('&')}` : ''
}

async function readJson(response: Response): Promise<unknown> {
  try {
    return await response.json()
  } catch {
    // No body, or not JSON (a proxy's HTML page): ApiError then builds its own message.
    return undefined
  }
}

/**
 * An API server has two error bodies: { Code, Message, Property, Entity } from its actions and
 * { Error, Message } from its 'portal users only' check. Both become the body ApiError reads.
 */
function errorBody(body: unknown): unknown {
  if (typeof body !== 'object' || body === null) {
    return undefined
  }

  const { Code, Error: error, Message, Property, Entity } = body as Record<string, unknown>

  return { Code: typeof Code === 'string' ? Code : typeof error === 'string' ? error : 'ERROR', Message, Property, Entity }
}
