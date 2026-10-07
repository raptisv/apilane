// @vitest-environment jsdom
import { DOMWrapper, mount } from '@vue/test-utils'
import type { VueWrapper } from '@vue/test-utils'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import type { Schemas } from '@/lib/api'
import { body, button, dialog, installDomShims, settle } from '@/test/dom'
import AgentsPage from './AgentsPage.vue'
import UsersPage from './UsersPage.vue'

const calls = vi.hoisted(() => ({ get: vi.fn(), post: vi.fn(), put: vi.fn(), remove: vi.fn(), success: vi.fn() }))

// Exercise the real pages, dialogs, validation and async state. Only the API boundary and
// toast delivery are replaced; the server list deliberately contains both people and agents.
vi.mock('@/lib/api', async (original) => ({
  ...await original<typeof import('@/lib/api')>(),
  api: { GET: calls.get, POST: calls.post, PUT: calls.put, DELETE: calls.remove },
}))
vi.mock('@/lib/toast', () => ({ success: calls.success, warning: vi.fn() }))

const usersPath = '/api/v1/admin/users'
const agentsPath = '/api/v1/admin/agents'
const removePath = '/api/v1/admin/agents/{userId}'
const mixedAgentEmail = 'Build@AGENT.LOCAL'
const mixedAgentName = 'Build'
const newAgentEmail = 'new-agent@agent.local'
const newAgentName = 'new-agent'
const oneTimeKey = 'apl_test-key-id_test-only-secret'
let wrapper: VueWrapper | undefined
let users: Schemas['UserResponse'][]

function user(ID: string, Email: string, IsAdmin = false, IsCurrentUser = false): Schemas['UserResponse'] {
  return { ID, Email, IsAdmin, IsCurrentUser, LastLogin: '2026-10-01T12:00:00Z' }
}

function ok<T>(data: T) {
  return { data, response: new Response(null, { status: 200 }) }
}

function rejected(message: string) {
  return { error: { Code: 'CONFLICT', Message: message }, response: new Response(null, { status: 409 }) }
}

function confirmation(): DOMWrapper<Element> {
  return new DOMWrapper(body().get('[role="alertdialog"]').element)
}

function readOnlyValues(): string[] {
  return body().findAll<HTMLInputElement>('input[readonly]').map((input) => input.element.value)
}

async function mountAgents(): Promise<void> {
  wrapper = mount(AgentsPage, { attachTo: document.body })
  await settle()
}

async function startCreating(): Promise<void> {
  await button('Add agent').trigger('click')
  await settle()
  await dialog().get('input').setValue('new-agent')
}

async function submitCreation(): Promise<void> {
  await dialog().get('form').trigger('submit')
  await settle()
}

async function startDeleting(): Promise<void> {
  await button(`Delete agent ${mixedAgentName}`).trigger('click')
  await settle()
}

async function submitDeletion(): Promise<void> {
  await confirmation().get('form').trigger('submit')
  await settle()
}

beforeEach(() => {
  vi.resetAllMocks()
  installDomShims()
  users = [
    user('current-admin', 'admin@example.test', true, true),
    user('mixed-agent', mixedAgentEmail),
    user('person', 'person@example.test'),
    user('other-agent', 'other@agent.local'),
    user('suffix-person', 'person@agent.local.example'),
  ]
  calls.get.mockImplementation(async (path: string) => {
    if (path !== usersPath) {
      throw new Error(`Unexpected GET ${path}`)
    }
    return ok({ Data: users, Total: users.length })
  })
  calls.post.mockImplementation(async () => {
    users = [...users, user('new-agent-id', newAgentEmail)]
    return ok({ ID: 'new-agent-id', Email: newAgentEmail, Key: oneTimeKey } satisfies Schemas['AgentCreatedResponse'])
  })
  calls.put.mockImplementation(async (_path: string, request: { params: { path: { userId: string } }; body: { IsAdmin: boolean } }) => {
    users = users.map((entry) => entry.ID === request.params.path.userId ? { ...entry, IsAdmin: request.body.IsAdmin } : entry)
    return ok(undefined)
  })
  calls.remove.mockImplementation(async (_path: string, request: { params: { path: { userId: string } } }) => {
    users = users.filter((entry) => entry.ID !== request.params.path.userId)
    return ok(undefined)
  })
})

afterEach(() => {
  wrapper?.unmount()
  wrapper = undefined
  document.body.innerHTML = ''
  vi.unstubAllGlobals()
})

describe('separate agent management', () => {
  it('splits the same server list by the case-insensitive agent suffix and keeps role changes on people', async () => {
    await mountAgents()
    const agentsTable = body().get('tbody').text()
    expect(agentsTable).toContain(mixedAgentName)
    expect(agentsTable).toContain('other')
    expect(agentsTable.toLowerCase()).not.toContain('@agent.local')
    expect(agentsTable).not.toContain('admin@example.test')
    expect(agentsTable).not.toContain('person@example.test')
    expect(agentsTable).not.toContain('person@agent.local.example')
    expect(body().findAll('button').filter((candidate) => candidate.text().startsWith('Make '))).toHaveLength(0)

    wrapper?.unmount()
    wrapper = mount(UsersPage, { attachTo: document.body })
    await settle()
    const peopleTable = body().get('tbody').text()
    expect(peopleTable).toContain('admin@example.test')
    expect(peopleTable).toContain('person@example.test')
    expect(peopleTable).toContain('person@agent.local.example')
    expect(peopleTable).not.toContain(mixedAgentEmail)
    expect(peopleTable).not.toContain('other@agent.local')
    const roleButtons = body().findAll('button').filter((candidate) => candidate.text().startsWith('Make '))
    expect(roleButtons.map((candidate) => candidate.get('.sr-only').text())).toEqual([
      ': person@example.test',
      ': person@agent.local.example',
    ])
    expect(body().findAll('button').some((candidate) => candidate.text() === 'Add agent' || candidate.text().startsWith('Delete agent'))).toBe(false)

    await roleButtons[0]?.trigger('click')
    await settle()
    await button('Make admin', confirmation()).trigger('click')
    await settle()
    expect(calls.put).toHaveBeenCalledWith('/api/v1/admin/users/{userId}/role', {
      params: { path: { userId: 'person' } }, body: { IsAdmin: true },
    })
    expect(calls.post).not.toHaveBeenCalled()
    expect(calls.remove).not.toHaveBeenCalled()
  })

  it.each(['Done', 'unmount'] as const)('creates an agent and forgets its one-time key after %s', async (close) => {
    await mountAgents()
    await startCreating()
    await submitCreation()
    expect(calls.post).toHaveBeenCalledExactlyOnceWith(agentsPath, { body: { Name: 'new-agent' } })
    expect(dialog().text()).toContain('Agent added')
    expect(dialog().text()).toContain('Name')
    expect(dialog().text()).not.toContain('Address')
    expect(readOnlyValues()).toEqual([newAgentName, oneTimeKey])
    expect(body().get('tbody').text()).toContain(newAgentName)

    if (close === 'Done') {
      await button('Done', dialog()).trigger('click')
      await settle()
      expect(body().find('[role="dialog"]').exists()).toBe(false)
      expect(readOnlyValues()).not.toContain(oneTimeKey)
      await startCreating()
      expect(readOnlyValues()).not.toContain(oneTimeKey)
      await button('Cancel', dialog()).trigger('click')
      await settle()
    }

    wrapper?.unmount()
    wrapper = undefined
    await settle()
    expect(body().find('[role="dialog"]').exists()).toBe(false)
    expect(readOnlyValues()).not.toContain(oneTimeKey)
    await mountAgents()
    expect(body().get('tbody').text()).toContain(newAgentName)
    expect(body().find('[role="dialog"]').exists()).toBe(false)
    expect(readOnlyValues()).not.toContain(oneTimeKey)
    expect(calls.post).toHaveBeenCalledOnce()
  })

  it('keeps the creation draft and list after rejection so the same form can be retried', async () => {
    calls.post.mockResolvedValueOnce(rejected('An agent with this name already exists.'))
    await mountAgents()
    const initialTable = body().get('tbody').text()
    await startCreating()
    await submitCreation()
    expect(dialog().get('[role="alert"]').text()).toBe('An agent with this name already exists.')
    expect(dialog().get<HTMLInputElement>('input').element.value).toBe('new-agent')
    expect(body().get('tbody').text()).toBe(initialTable)
    expect(calls.get).toHaveBeenCalledOnce()
    expect(readOnlyValues()).not.toContain(oneTimeKey)

    await submitCreation()
    expect(calls.post).toHaveBeenCalledTimes(2)
    expect(readOnlyValues()).toContain(oneTimeKey)
    expect(body().get('tbody').text()).toContain(newAgentName)
  })

  it('requires the exact displayed agent name before deleting its ID and refreshing the list', async () => {
    await mountAgents()
    await startDeleting()
    expect(confirmation().text()).toContain('Delete agent Build?')
    expect(confirmation().text()).not.toContain(mixedAgentEmail)
    expect(button('Delete', confirmation()).attributes('disabled')).toBeDefined()
    await submitDeletion()
    await confirmation().get('input').setValue(mixedAgentName.toLowerCase())
    expect(button('Delete', confirmation()).attributes('disabled')).toBeDefined()
    await submitDeletion()
    expect(calls.remove).not.toHaveBeenCalled()

    await confirmation().get('input').setValue(mixedAgentName)
    expect(button('Delete', confirmation()).attributes('disabled')).toBeUndefined()
    await submitDeletion()
    expect(calls.remove).toHaveBeenCalledExactlyOnceWith(removePath, { params: { path: { userId: 'mixed-agent' } } })
    expect(body().find('[role="alertdialog"]').exists()).toBe(false)
    expect(body().get('tbody').text()).not.toContain(mixedAgentName)
    expect(body().get('tbody').text()).toContain('other')
    expect(calls.success).toHaveBeenCalledWith('Agent deleted.')
  })

  it('keeps the deletion confirmation and list intact when deletion fails', async () => {
    calls.remove.mockResolvedValueOnce(rejected('The agent could not be deleted.'))
    await mountAgents()
    const initialTable = body().get('tbody').text()
    await startDeleting()
    await confirmation().get('input').setValue(mixedAgentName)
    await submitDeletion()
    expect(confirmation().get('[role="alert"]').text()).toBe('The agent could not be deleted.')
    expect(confirmation().get<HTMLInputElement>('input').element.value).toBe(mixedAgentName)
    expect(body().get('tbody').text()).toBe(initialTable)
    expect(calls.get).toHaveBeenCalledOnce()
    expect(calls.success).not.toHaveBeenCalled()

    await submitDeletion()
    expect(calls.remove).toHaveBeenCalledTimes(2)
    expect(calls.remove).toHaveBeenLastCalledWith(removePath, { params: { path: { userId: 'mixed-agent' } } })
    expect(body().find('[role="alertdialog"]').exists()).toBe(false)
    expect(body().get('tbody').text()).not.toContain(mixedAgentName)
  })
})
