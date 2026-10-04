/**
 * The rules of the custom endpoints screens, as pure functions: the parameters and addresses of an
 * endpoint (computed in the browser while the SQL is typed, exactly as the Portal computes them),
 * the search over the list, and the body of the SQL test on the API server.
 */

/** What the editor shows under the SQL: the same three values as CustomEndpointResponse. */
export interface EndpointAddress {
  Parameters: string[]
  /** The address without the application token, with one {Name} placeholder per parameter. */
  Url: string
  /** The address a client calls: Url with the application token as the first value. */
  CallUrl: string
}

/**
 * The {Name} placeholders of a query: letters and underscore only, each once, in the order they
 * first appear, without Owner (the API server puts the calling user's ID there). A copy of
 * DBWS_CustomEndpoint.GetParameters: '{owner}' is a normal parameter, '{Product1}' is none.
 */
export function endpointParameters(query: string | null | undefined): string[] {
  const found: string[] = []

  for (const match of (query ?? '').matchAll(/\{([a-zA-Z_]+?)\}/g)) {
    if (!found.includes(match[1])) {
      found.push(match[1])
    }
  }

  return found.filter((name) => name !== 'Owner')
}

/**
 * The parameters and addresses of an endpoint with this name and query: a copy of what
 * CustomEndpointService gives (Url and CallUrl), with the name trimmed as a save trims it. A query
 * with {appToken} gives that name twice in CallUrl, the token first. Nothing is checked, so an
 * empty name gives an address that ends in /api/Custom/, as the preview endpoint does.
 */
export function endpointAddress(serverUrl: string, appToken: string, name: string, query: string): EndpointAddress {
  const parameters = endpointParameters(query)
  // String.Trim('/') of the model removes the slashes at both ends.
  const url = `${serverUrl.replace(/^\/+|\/+$/g, '')}/api/Custom/${name.trim()}`
  const placeholders = parameters.map((parameter) => `${parameter}={${parameter}}`)

  return {
    Parameters: parameters,
    Url: withQuery(url, placeholders),
    CallUrl: withQuery(url, [`appToken=${appToken}`, ...placeholders]),
  }
}

function withQuery(url: string, pairs: string[]): string {
  return pairs.length > 0 ? `${url}?${pairs.join('&')}` : url
}

/** The query string of a SQL test: every parameter of the query with the value typed for it, '' when none. */
export function testParameters(
  parameters: readonly string[],
  values: Readonly<Record<string, string | number | undefined>>,
): Record<string, string | number> {
  // An empty value reaches the API server as 'Name=', which it turns into SQL null, as on the Razor page.
  return Object.fromEntries(parameters.map((name) => [name, values[name] ?? '']))
}

/**
 * The body of POST {ServerUrl}/api/Custom/TestQuery. The API server checks the name as it checks
 * a saved one, so it gets a fixed valid name, as from the Razor page; only the query is run.
 */
export function testQueryBody(query: string): { Name: string; Query: string } {
  return { Name: 'test', Query: query }
}

/** One piece of a text cut where the search matched, for drawing the matches highlighted. */
export interface TextPart {
  text: string
  match: boolean
}

interface Searchable {
  Name: string
  Description?: string | null
  Query: string
}

/**
 * The endpoints whose name, description or SQL contains the text, ignoring letter case. The text
 * is used exactly as typed (spaces included); an empty text matches nothing.
 */
export function searchEndpoints<T extends Searchable>(endpoints: readonly T[], text: string): T[] {
  if (text === '') {
    return []
  }

  const pattern = new RegExp(escapeRegExp(text), 'iu')

  return endpoints.filter((endpoint) => [endpoint.Name, endpoint.Description ?? '', endpoint.Query].some((value) => pattern.test(value)))
}

/**
 * Cuts a text into the parts that match the search text and the parts between them, with the same
 * rule as searchEndpoints, so a result always shows why it matched.
 */
export function highlightMatches(value: string, text: string): TextPart[] {
  if (text === '' || value === '') {
    return value === '' ? [] : [{ text: value, match: false }]
  }

  // With a capturing group, split keeps the matches: they are the odd entries.
  return value
    .split(new RegExp(`(${escapeRegExp(text)})`, 'giu'))
    .map((part, index) => ({ text: part, match: index % 2 === 1 }))
    .filter((part) => part.text !== '')
}

function escapeRegExp(text: string): string {
  return text.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')
}

/** Whether a rename takes the endpoint away from its security rules, which the API server matches by name in any letter case. */
export function renameLosesRules(savedName: string, typedName: string): boolean {
  const typed = typedName.trim()

  return typed !== '' && typed.toLowerCase() !== savedName.toLowerCase()
}
