// @vitest-environment jsdom
import { DOMWrapper, mount } from '@vue/test-utils'
import type { VueWrapper } from '@vue/test-utils'
import type { Component } from 'vue'
import { createMemoryHistory, createRouter } from 'vue-router'
import type { Router } from 'vue-router'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import type { Schemas } from '@/lib/api'
import { setSession } from '@/lib/session'
import { body, button, dialog, installDomShims, settle } from '@/test/dom'
import AuthorizePage from './AuthorizePage.vue'
import ConnectionsPage from './ConnectionsPage.vue'

const calls = vi.hoisted(() => ({ get: vi.fn(), post: vi.fn(), remove: vi.fn(), assign: vi.fn(), success: vi.fn(), metadata: vi.fn() }))
vi.mock('@/lib/api', async (original) => ({
  ...await original<typeof import('@/lib/api')>(),
  api: { GET: calls.get, POST: calls.post, DELETE: calls.remove },
}))
vi.mock('@/lib/toast', () => ({ success: calls.success, warning: vi.fn() }))

const authorizationPath = '/api/v1/mcp/authorize'
const denyPath = '/api/v1/mcp/authorize/deny'
const connectionsPath = '/api/v1/mcp/connections'
const revokePath = '/api/v1/mcp/connections/{id}'
const callback = 'http://127.0.0.1:19876/callback'
const callbackCode = 'one-use-code-not-for-display'
let wrapper: VueWrapper | undefined
let router: Router
let authorization: Schemas['McpAuthorizationResponse']
let connections: Schemas['McpConnectionResponse'][]

function ok<T>(data: T) {
  return { data, response: new Response(null, { status: 200 }) }
}

function refused(status: number, message: string) {
  return { error: { Code: status === 404 ? 'NOT_FOUND' : 'ERROR', Message: message }, response: new Response(null, { status }) }
}

function connection(Id: string, Name: string, overrides: Partial<Schemas['McpConnectionResponse']> = {}): Schemas['McpConnectionResponse'] {
  return {
    Id, Name, ClientName: 'Example client', AgentId: 'build-agent-id', AgentName: 'Build', IsUsable: true,
    AuthorizedByEmail: 'owner@example.test', CreatedAt: '2026-10-07T10:00:00Z', LastUsedAt: null,
    ExpiresAt: '2099-10-07T10:00:00Z', RevokedAt: null, Applications: [{ Token: 'private-app-id', Name: 'Shop' }],
    ...overrides,
  }
}

async function mountPage(component: Component, path: string): Promise<void> {
  router = createRouter({ history: createMemoryHistory(), routes: [{ path: '/:pathMatch(.*)*', component: { render: () => null } }] })
  await router.push(path)
  await router.isReady()
  wrapper = mount(component, { attachTo: document.body, global: { plugins: [router] } })
  await settle()
}

function field(label: string): DOMWrapper<HTMLInputElement> {
  const entry = body().findAll('label').find((candidate) => candidate.text() === label)
  if (!entry) {
    throw new Error(`Missing field ${label}`)
  }
  return new DOMWrapper(body().get<HTMLInputElement>(`input[id="${entry.attributes('for')}"]`).element)
}

async function choose(label: string, option: string): Promise<void> {
  const entry = body().findAll('label').find((candidate) => candidate.text() === label)
  if (!entry) {
    throw new Error(`Missing select ${label}`)
  }
  await body().get(`[role="combobox"][id="${entry.attributes('for')}"]`).trigger('keydown', { key: 'Enter' })
  await settle()
  const item = body().findAll('[role="option"]').find((candidate) => candidate.text() === option)
  if (!item) {
    throw new Error(`Missing option ${option}`)
  }
  await item.trigger('keydown', { key: 'Enter' })
  await settle()
}

function confirmation(): DOMWrapper<Element> {
  return new DOMWrapper(body().get('[role="alertdialog"]').element)
}

beforeEach(() => {
  vi.resetAllMocks()
  installDomShims()
  vi.stubGlobal('location', { origin: 'https://portal.example.test', assign: calls.assign })
  vi.stubGlobal('fetch', calls.metadata)
  calls.metadata.mockImplementation(async () => new Response(JSON.stringify({ resource: 'https://canonical.example.test/api/mcp' }), { status: 200 }))
  setSession({ Email: 'owner@example.test', IsAdmin: false, InstanceTitle: 'Portal', Version: '1' })
  authorization = {
    RequestId: 'pending-request', ClientName: 'Example client', RedirectUri: callback,
    Agents: [
      { Id: 'build-agent-id', Name: 'Build', Applications: [{ Token: 'private-app-id', Name: 'Shop' }] },
      { Id: 'docs-agent-id', Name: 'Docs', Applications: [{ Token: 'docs-app-id', Name: 'Documentation' }] },
    ],
  }
  connections = [
    connection('first-connection-id', 'Shop folder'),
    connection('second-connection-id', 'Another folder'),
    connection('expired-connection-id', 'Expired folder', { ExpiresAt: '2020-01-01T00:00:00Z' }),
    connection('revoked-connection-id', 'Revoked folder', { RevokedAt: '2026-10-07T11:00:00Z' }),
    connection('unavailable-connection-id', 'Unavailable folder', { IsUsable: false, Applications: [] }),
  ]
  calls.get.mockImplementation(async (path: string) => {
    if (path === authorizationPath) {
      return ok(authorization)
    }
    if (path === connectionsPath) {
      return ok(connections)
    }
    throw new Error(`Unexpected GET ${path}`)
  })
  calls.post.mockImplementation(async (path: string) => ok({ RedirectUrl: path === denyPath
    ? `${callback}?error=access_denied&state=client-state`
    : `${callback}?code=${callbackCode}&state=client-state` }))
  calls.remove.mockImplementation(async (_path: string, request: { params: { path: { id: string } } }) => {
    connections = connections.map((entry) => entry.Id === request.params.path.id ? { ...entry, RevokedAt: '2026-10-07T11:00:00Z' } : entry)
    return ok(undefined)
  })
})

afterEach(() => {
  wrapper?.unmount()
  wrapper = undefined
  document.body.innerHTML = ''
  vi.restoreAllMocks()
  vi.unstubAllGlobals()
})

describe('MCP approval', () => {
  it('requires an explicit existing agent, shows its application scope and approves without exposing the authorization code', async () => {
    const storageWrite = vi.spyOn(Storage.prototype, 'setItem')
    await mountPage(AuthorizePage, '/mcp/authorize?requestId=pending-request')
    expect(calls.get).toHaveBeenCalledExactlyOnceWith(authorizationPath, { params: { query: { requestId: 'pending-request' } } })
    expect(body().text()).toContain('127.0.0.1:19876')
    expect(field('Connection name').element.value).toBe('Example client')
    expect(button('Approve connection').attributes('disabled')).toBeDefined()
    await body().get('form').trigger('submit')
    await settle()
    expect(body().text()).toContain('Select an existing agent.')
    expect(calls.post).not.toHaveBeenCalled()

    await choose('Agent', 'Build')
    expect(body().text()).toContain('This client acts as Build')
    expect(body().text()).toContain('Shop')
    expect(body().text()).not.toContain('Documentation')
    expect(body().text()).toContain('future shares are not included')
    expect(body().text()).not.toContain('private-app-id')
    await field('Connection name').setValue('  Shop folder  ')
    await body().get('form').trigger('submit')
    await settle()
    expect(calls.post).toHaveBeenCalledExactlyOnceWith(authorizationPath, {
      body: { RequestId: 'pending-request', AgentId: 'build-agent-id', Name: 'Shop folder', ApplicationTokens: ['private-app-id'] },
    })
    expect(calls.assign).toHaveBeenCalledExactlyOnceWith(`${callback}?code=${callbackCode}&state=client-state`)
    expect(body().text()).not.toContain(callbackCode)
    expect(storageWrite).not.toHaveBeenCalled()
    expect(button('Approve connection').attributes('disabled')).toBeDefined()
  })

  it('denies without selecting an agent and only follows the server return address', async () => {
    await mountPage(AuthorizePage, '/mcp/authorize?requestId=pending-request&redirect_uri=https://untrusted.test')
    await button('Deny').trigger('click')
    await settle()
    expect(calls.post).toHaveBeenCalledExactlyOnceWith(denyPath, { body: { RequestId: 'pending-request' } })
    expect(calls.assign).toHaveBeenCalledExactlyOnceWith(`${callback}?error=access_denied&state=client-state`)
    expect(body().text()).not.toContain('untrusted.test')
  })

  it('retains the chosen agent and name after a failed approval for retry', async () => {
    calls.post.mockResolvedValueOnce(refused(409, 'The selected agent is no longer available.'))
    await mountPage(AuthorizePage, '/mcp/authorize?requestId=pending-request')
    await choose('Agent', 'Build')
    await field('Connection name').setValue('My project')
    await body().get('form').trigger('submit')
    await settle()
    expect(body().get('[role="alert"]').text()).toBe('The selected agent is no longer available.')
    expect(body().get('[role="combobox"]').text()).toBe('Build')
    expect(field('Connection name').element.value).toBe('My project')
    expect(calls.assign).not.toHaveBeenCalled()
    await body().get('form').trigger('submit')
    await settle()
    expect(calls.post).toHaveBeenCalledTimes(2)
    expect(calls.assign).toHaveBeenCalledOnce()
  })

  it('clears the selection when the request changes and never submits the earlier request', async () => {
    await mountPage(AuthorizePage, '/mcp/authorize?requestId=pending-request')
    await choose('Agent', 'Build')
    authorization = { ...authorization, RequestId: 'new-request', ClientName: 'Other client' }
    await router.push('/mcp/authorize?requestId=new-request')
    await settle()
    expect(body().get('[role="combobox"]').text()).toBe('Select an agent')
    expect(field('Connection name').element.value).toBe('Other client')
    await body().get('form').trigger('submit')
    await settle()
    expect(calls.post).not.toHaveBeenCalled()
  })

  it.each(['missing', 'expired'] as const)('blocks a %s request without approval controls', async (state) => {
    calls.get.mockResolvedValueOnce(refused(404, 'The request is no longer available.'))
    await mountPage(AuthorizePage, state === 'missing' ? '/mcp/authorize' : '/mcp/authorize?requestId=expired')
    expect(body().text()).toContain('This connection request is no longer available')
    expect(body().find('form').exists()).toBe(false)
    expect(calls.post).not.toHaveBeenCalled()
    expect(calls.assign).not.toHaveBeenCalled()
    if (state === 'missing') {
      expect(calls.get).not.toHaveBeenCalled()
    }
  })

  it('allows denial with no eligible agents but cannot approve', async () => {
    authorization.Agents = []
    await mountPage(AuthorizePage, '/mcp/authorize?requestId=pending-request')
    expect(body().text()).toContain('No agents available')
    expect(body().text()).toContain('an application you own')
    expect(button('Approve connection').attributes('disabled')).toBeDefined()
    await body().get('form').trigger('submit')
    await settle()
    expect(calls.post).not.toHaveBeenCalled()
    await button('Deny').trigger('click')
    await settle()
    expect(calls.post).toHaveBeenCalledExactlyOnceWith(denyPath, { body: { RequestId: 'pending-request' } })
  })

  it('replaces the form when the request expires before approval', async () => {
    calls.post.mockResolvedValueOnce(refused(404, 'Authorization request was not found.'))
    await mountPage(AuthorizePage, '/mcp/authorize?requestId=pending-request')
    await choose('Agent', 'Build')
    await body().get('form').trigger('submit')
    await settle()
    expect(body().text()).toContain('Start connecting again in your client.')
    expect(body().find('form').exists()).toBe(false)
    expect(calls.assign).not.toHaveBeenCalled()
  })

  it.each([
    'https://unexpected.test/callback?code=secret',
    'javascript:alert("secret")',
    'http://127.0.0.1:19876/another-callback?code=secret',
    'http://user:secret@127.0.0.1:19876/callback',
  ])('rejects an unsafe or changed client return address: %s', async (RedirectUrl) => {
    calls.post.mockResolvedValueOnce(ok({ RedirectUrl }))
    await mountPage(AuthorizePage, '/mcp/authorize?requestId=pending-request')
    await button('Deny').trigger('click')
    await settle()
    expect(body().get('[role="alert"]').text()).toContain('return address could not be verified')
    expect(calls.assign).not.toHaveBeenCalled()
    expect(body().text()).not.toContain('secret')
  })
})

describe('MCP connections', () => {
  it('shows effective application scope and revokes only the confirmed connection; a failure keeps the list and dialog', async () => {
    calls.remove.mockResolvedValueOnce(refused(503, 'The connection could not be revoked.'))
    await mountPage(ConnectionsPage, '/mcp/connections')
    expect(body().text()).toContain('Shop')
    expect(body().text()).toContain('Build')
    expect(body().text()).not.toContain('private-app-id')
    expect(body().text()).not.toContain('@agent.local')
    expect(body().findAll('button').filter((entry) => entry.text().startsWith('Revoke connection '))).toHaveLength(3)
    const unavailable = body().findAll('tbody tr').find((row) => row.text().includes('Unavailable folder'))
    expect(unavailable?.text()).toContain('No applications currently available')
    expect(unavailable?.text()).not.toContain('Active')
    expect(button('Revoke connection Unavailable folder').exists()).toBe(true)
    await button('Revoke connection Shop folder').trigger('click')
    await settle()
    await confirmation().get('form').trigger('submit')
    expect(calls.remove).not.toHaveBeenCalled()
    await confirmation().get('input').setValue('Another folder')
    await confirmation().get('form').trigger('submit')
    expect(calls.remove).not.toHaveBeenCalled()

    const before = body().get('tbody').text()
    await confirmation().get('input').setValue('Shop folder')
    await confirmation().get('form').trigger('submit')
    await settle()
    expect(confirmation().get('[role="alert"]').text()).toBe('The connection could not be revoked.')
    expect(body().get('tbody').text()).toBe(before)
    expect(calls.get).toHaveBeenCalledOnce()
    await confirmation().get('form').trigger('submit')
    await settle()
    expect(calls.remove).toHaveBeenLastCalledWith(revokePath, { params: { path: { id: 'first-connection-id' } } })
    expect(body().find('[role="alertdialog"]').exists()).toBe(false)
    const rows = body().findAll('tbody tr')
    expect(rows.find((row) => row.text().includes('Shop folder'))?.text()).toContain('Revoked')
    expect(rows.find((row) => row.text().includes('Another folder'))?.text()).toContain('Active')
    expect(calls.success).toHaveBeenCalledExactlyOnceWith('Connection revoked.')
  })

  it('retries list loading and offers the canonical MCP address and a fresh server name for each setup', async () => {
    connections = []
    calls.get.mockResolvedValueOnce(refused(503, 'Please try again.'))
    await mountPage(ConnectionsPage, '/mcp/connections')
    expect(body().text()).toContain('Please try again.')
    await button('Try again').trigger('click')
    await settle()
    expect(body().text()).toContain('No MCP connections yet')
    await button('MCP setup').trigger('click')
    await settle()
    const suggestedName = field('Server name').element.value
    expect(suggestedName).toMatch(/^apilane_[a-f0-9]{12}$/)
    expect(field('MCP address').element.value).toBe('https://canonical.example.test/api/mcp')
    expect(field('MCP address').attributes('readonly')).toBeDefined()
    expect(field('Server name').attributes('readonly')).toBeDefined()
    expect(button('Copy MCP address', dialog()).exists()).toBe(true)
    expect(button('Copy Server name', dialog()).exists()).toBe(true)
    expect(calls.metadata).toHaveBeenCalledExactlyOnceWith('/api/mcp/.well-known/oauth-protected-resource', {
      credentials: 'same-origin', headers: { Accept: 'application/json' },
    })
    expect(dialog().text()).toContain('Streamable HTTP')
    expect(dialog().text()).toContain('OAuth or browser authentication')
    expect(dialog().text()).toContain('forward the callback port')
    expect(dialog().findAll('input').map((entry) => entry.element.value).join(' ')).not.toMatch(/Authorization|Bearer|clientSecret|accessToken|agent.local/)
    expect(calls.post).not.toHaveBeenCalled()
    await button('Done', dialog()).trigger('click')
    await settle()
    expect(body().find('[role="dialog"]').exists()).toBe(false)
    await button('MCP setup').trigger('click')
    await settle()
    expect(field('Server name').element.value).toMatch(/^apilane_[a-f0-9]{12}$/)
    expect(field('Server name').element.value).not.toBe(suggestedName)
  })

  it('withholds setup values when canonical metadata is unsafe and can retry', async () => {
    calls.metadata.mockResolvedValueOnce(new Response(JSON.stringify({ resource: 'http://remote.example.test/api/mcp' }), { status: 200 }))
    await mountPage(ConnectionsPage, '/mcp/connections')
    await button('MCP setup').trigger('click')
    await settle()
    expect(dialog().text()).toContain('did not publish a valid MCP address')
    expect(dialog().find('input[readonly]').exists()).toBe(false)
    await button('Try again', dialog()).trigger('click')
    await settle()
    expect(field('MCP address').element.value).toBe('https://canonical.example.test/api/mcp')
    expect(dialog().text()).not.toContain('remote.example.test')
  })
})
