import { api, ApiError, unwrap } from './api'
import type { Schemas } from './api'

export type McpAuthorization = Schemas['McpAuthorizationResponse']
export type McpConnection = Schemas['McpConnectionResponse']

export function loadMcpAuthorization(requestId: string): Promise<McpAuthorization> {
  return unwrap(api.GET('/api/v1/mcp/authorize', { params: { query: { requestId } } }))
}

export function approveMcpAuthorization(body: Schemas['McpApproveRequest']): Promise<Schemas['McpRedirectResponse']> {
  return unwrap(api.POST('/api/v1/mcp/authorize', { body }))
}

export function denyMcpAuthorization(requestId: string): Promise<Schemas['McpRedirectResponse']> {
  return unwrap(api.POST('/api/v1/mcp/authorize/deny', { body: { RequestId: requestId } }))
}

export function loadMcpConnections(): Promise<McpConnection[]> {
  return unwrap(api.GET('/api/v1/mcp/connections'))
}

export function revokeMcpConnection(id: string): Promise<void> {
  return unwrap(api.DELETE('/api/v1/mcp/connections/{id}', { params: { path: { id } } }))
}

/** Read the canonical resource from this Portal, including when the UI uses a development proxy. */
export async function loadMcpEndpoint(): Promise<string> {
  let response: Response
  try {
    response = await fetch('/api/mcp/.well-known/oauth-protected-resource', { credentials: 'same-origin', headers: { Accept: 'application/json' } })
  } catch {
    throw new ApiError(0, { Code: 'NETWORK', Message: 'The Portal could not be reached.' })
  }
  if (!response.ok) {
    throw new ApiError(response.status)
  }
  try {
    const metadata: unknown = await response.json()
    if (typeof metadata !== 'object' || metadata === null || !('resource' in metadata) || typeof metadata.resource !== 'string') {
      throw new Error('Missing resource')
    }
    const endpoint = new URL(metadata.resource)
    const loopback = endpoint.hostname === 'localhost' || endpoint.hostname === '[::1]' || /^127(?:\.\d{1,3}){3}$/.test(endpoint.hostname)
    if (endpoint.username || endpoint.password || endpoint.hash || (endpoint.protocol !== 'https:' && !(endpoint.protocol === 'http:' && loopback))) {
      throw new Error('Invalid resource')
    }
    return endpoint.href
  } catch {
    throw new Error('The Portal did not publish a valid MCP address. Ask the server operator to check its public address.')
  }
}

/** Only the validated server response can complete OAuth; query parameters never supply this URL. */
export function returnToMcpClient(redirectUrl: string, redirectUri: string): void {
  const target = new URL(redirectUrl)
  const expected = new URL(redirectUri)
  if (!['http:', 'https:'].includes(target.protocol) || target.username || target.password
    || target.origin !== expected.origin || target.pathname !== expected.pathname) {
    throw new Error('The client return address could not be verified. Start connecting again in your client.')
  }
  location.assign(target.href)
}

export function mcpConnectionStatus(connection: McpConnection): 'Active' | 'Expired' | 'Revoked' | 'Unavailable' {
  if (connection.RevokedAt) {
    return 'Revoked'
  }
  if (Date.parse(connection.ExpiresAt) <= Date.now()) {
    return 'Expired'
  }
  return connection.IsUsable ? 'Active' : 'Unavailable'
}
