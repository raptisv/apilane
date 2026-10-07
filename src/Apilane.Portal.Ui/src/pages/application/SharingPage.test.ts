// @vitest-environment jsdom
import { DOMWrapper, mount } from '@vue/test-utils'
import type { VueWrapper } from '@vue/test-utils'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import type { Schemas } from '@/lib/api'
import type { AgentPermissionGrant } from '@/lib/agentPermissions'
import { permissionResources, readOnlyPolicy } from '@/test/agentPermissions'
import { body, button, choosePermission, deletionControl, dialog, installDomShims, permissionControl, settle } from '@/test/dom'
import SharingPage from './SharingPage.vue'

const calls = vi.hoisted(() => ({
  get: vi.fn(), post: vi.fn(), put: vi.fn(), remove: vi.fn(), reloadApplications: vi.fn(),
  success: vi.fn(), warning: vi.fn(),
}))

// Keep the page, async/mutation composables, error conversion, form dialog, and permission
// controls real. Only the network boundary and the parent application's context are replaced.
vi.mock('@/lib/api', async (original) => ({
  ...await original<typeof import('@/lib/api')>(),
  api: { GET: calls.get, POST: calls.post, PUT: calls.put, DELETE: calls.remove },
}))
vi.mock('@/composables/useApplication', async () => {
  const { shallowRef } = await import('vue')
  return { useApplication: () => ({ application: shallowRef({ Token: 'shared-app' }) }) }
})
vi.mock('@/composables/useApplications', () => ({ useApplications: () => ({ reload: calls.reloadApplications }) }))
vi.mock('@/lib/toast', () => ({ success: calls.success, warning: calls.warning }))

const collaboratorsPath = '/api/v1/applications/{appToken}/collaborators'
const availableAgentsPath = '/api/v1/applications/{appToken}/collaborators/available-agents'
const permissionsPath = '/api/v1/applications/{appToken}/permissions'
const editPath = '/api/v1/applications/{appToken}/collaborators/{id}/permissions'
let wrapper: VueWrapper | undefined
let collaborators: Schemas['CollaboratorResponse'][]
let availableAgents: Schemas['AvailableAgentResponse'][]

function ok<T>(data: T) {
  return { data, response: new Response(null, { status: 200 }) }
}

function rejected(message: string, property?: string) {
  return { error: { Code: 'VALIDATION', Message: message, Property: property }, response: new Response(null, { status: 400 }) }
}

function catalogue(): Schemas['ApplicationPermissionsResponse'] {
  return { IsAgent: false, Permissions: [], Resources: permissionResources, Operations: [], Restrictions: [] }
}

function deferred<T>() {
  let resolve: (value: T) => void = () => { throw new Error('Promise has not been initialized') }
  const promise = new Promise<T>((complete) => { resolve = complete })
  return { promise, resolve }
}

function getResponse(path: string) {
  if (path === collaboratorsPath) {
    return ok({ Data: collaborators, Total: collaborators.length })
  }
  if (path === permissionsPath) {
    return ok(catalogue())
  }
  if (path === availableAgentsPath) {
    return ok({ Data: availableAgents, Total: availableAgents.length })
  }
  throw new Error(`Unexpected GET ${path}`)
}

beforeEach(() => {
  vi.clearAllMocks()
  installDomShims()
  collaborators = [
    { ID: 17, Email: 'existing@agent.local', Permissions: readOnlyPolicy() },
    { ID: 18, Email: 'person@example.test', Permissions: null },
  ]
  availableAgents = [{ Name: 'new', Email: 'new@agent.local', Applications: [] }]
  calls.get.mockImplementation(async (path: string) => getResponse(path))
  calls.post.mockImplementation(async (_path: string, request: { body: { Email: string; Permissions?: AgentPermissionGrant[] } }) => {
    const added = { ID: 19, Email: request.body.Email.trim(), Permissions: request.body.Permissions ?? null, NotificationSent: false }
    collaborators = [...collaborators, added]
    return ok(added)
  })
  calls.put.mockImplementation(async (_path: string, request: { body: { Permissions: AgentPermissionGrant[] } }) => {
    // The real HTTP boundary serializes Vue's reactive draft before the server stores it.
    const permissions: AgentPermissionGrant[] = JSON.parse(JSON.stringify(request.body.Permissions))
    const updated = { ID: 17, Email: 'existing@agent.local', Permissions: permissions }
    collaborators = [updated, ...collaborators.filter((item) => item.ID !== 17)]
    return ok(updated)
  })
  calls.remove.mockImplementation(async (_path: string, request: { params: { path: { id: number } } }) => {
    collaborators = collaborators.filter((item) => item.ID !== request.params.path.id)
    return ok(undefined)
  })
  calls.reloadApplications.mockResolvedValue(undefined)
})

afterEach(() => {
  wrapper?.unmount()
  wrapper = undefined
  document.body.innerHTML = ''
  vi.unstubAllGlobals()
})

async function mountPage(): Promise<void> {
  wrapper = mount(SharingPage, { attachTo: document.body })
  await settle()
}

async function openAgentSharing(): Promise<void> {
  await button('Share with agent').trigger('click')
  await settle()
}

async function chooseAgent(name: string): Promise<void> {
  await dialog().get('[role="combobox"][aria-label="Agent"]').trigger('keydown', { key: 'Enter' })
  await settle()
  const option = body().findAll('[role="option"]').find((candidate) => candidate.text() === name)
  if (!option) {
    throw new Error(`Agent "${name}" is not offered`)
  }
  await option.trigger('keydown', { key: 'Enter' })
  await settle()
}

async function startSharing(name = 'new'): Promise<void> {
  await openAgentSharing()
  await chooseAgent(name)
}

async function startEditing(): Promise<void> {
  await button('Edit rights of existing').trigger('click')
  await settle()
}

async function submit(): Promise<void> {
  await dialog().get('form').trigger('submit')
  await settle()
}

function submittedPolicy(method: typeof calls.post | typeof calls.put): AgentPermissionGrant[] | undefined {
  const request = method.mock.calls.at(-1)?.[1] as { body: { Permissions?: AgentPermissionGrant[] } } | undefined
  return request?.body.Permissions
}

describe('SharingPage agent rights', () => {
  it('shares a new agent with read-only defaults and explains that rights can be edited later', async () => {
    await mountPage()
    await startSharing()
    expect(dialog().text()).toContain('Agents start with read-only access.')
    expect(dialog().text()).toContain('you can edit this agent’s rights later from Sharing.')
    expect(permissionControl('entities').text()).toBe('Read')
    expect(permissionControl('rebuild').text()).toBe('None')
    await submit()
    expect(calls.post).toHaveBeenCalledWith(collaboratorsPath, {
      params: { path: { appToken: 'shared-app' } }, body: { Email: 'new@agent.local', Permissions: readOnlyPolicy() },
    })
    expect(body().find('[role="dialog"]').exists()).toBe(false)
    expect(calls.success).toHaveBeenCalledWith('Shared with new. You can edit this agent’s rights later from Sharing.')
    expect(calls.reloadApplications).toHaveBeenCalledOnce()
    expect(body().text()).toContain('new')
    expect(body().text()).not.toContain('@agent.local')
  })

  it('offers agent names with their original case and submits the raw address of the selected agent', async () => {
    availableAgents = [{ Name: 'BuildBot', Email: 'BuildBot@AGENT.local', Applications: [] }]
    await mountPage()
    await openAgentSharing()
    expect(dialog().find('input[type="email"]').exists()).toBe(false)
    await dialog().get('[role="combobox"][aria-label="Agent"]').trigger('keydown', { key: 'Enter' })
    await settle()
    const options = body().findAll('[role="option"]')
    expect(options.map((option) => option.text())).toEqual(['BuildBot'])
    expect(body().text()).not.toMatch(/@agent\.local/i)
    await body().get('[role="option"]').trigger('keydown', { key: 'Enter' })
    await settle()
    expect(dialog().get('[role="combobox"][aria-label="Agent"]').text()).toContain('BuildBot')
    await submit()
    expect(calls.post).toHaveBeenCalledWith(collaboratorsPath, {
      params: { path: { appToken: 'shared-app' } }, body: { Email: 'BuildBot@AGENT.local', Permissions: readOnlyPolicy() },
    })
    expect(calls.success).toHaveBeenCalledWith('Shared with BuildBot. You can edit this agent’s rights later from Sharing.')
    expect(body().text()).not.toMatch(/@agent\.local/i)
  })

  it('requires an available agent to be selected before sharing', async () => {
    await mountPage()
    await openAgentSharing()
    await submit()
    expect(calls.post).not.toHaveBeenCalled()
    expect(dialog().find('[role="alert"]').exists()).toBe(true)
    expect(dialog().text()).not.toContain('@agent.local')
    await chooseAgent('new')
    await submit()
    expect(calls.post).toHaveBeenCalledOnce()
  })

  it('shows only the selected agent’s other visible applications by name', async () => {
    availableAgents = [
      { Name: 'new', Email: 'new@agent.local', Applications: [{ Token: 'inventory-app-token', Name: 'Inventory' }] },
      { Name: 'another', Email: 'another@agent.local', Applications: [{ Token: 'billing-app-token', Name: 'Billing' }] },
    ]
    await mountPage()
    await startSharing()
    expect(dialog().text()).toContain('Other applications')
    expect(dialog().text()).toContain('Only applications that both you and this agent can access are listed.')
    expect(dialog().text()).toContain('Inventory')
    expect(dialog().text()).not.toContain('Billing')
    expect(dialog().text()).not.toContain('inventory-app-token')
    expect(dialog().text()).not.toContain('billing-app-token')
    expect(dialog().text()).not.toContain('@agent.local')
    expect(dialog().findAll('a').some((link) => link.text() === 'Inventory')).toBe(false)
    await chooseAgent('another')
    expect(dialog().text()).toContain('Billing')
    expect(dialog().text()).not.toContain('Inventory')
    expect(dialog().text()).not.toContain('billing-app-token')
    expect(dialog().findAll('a').some((link) => link.text() === 'Billing')).toBe(false)
    expect(calls.post).not.toHaveBeenCalled()
  })

  it('explains when the selected agent has no other applications visible to this user', async () => {
    await mountPage()
    await startSharing()
    expect(dialog().text()).toContain('Other applications')
    expect(dialog().text()).toContain('This agent has no access to other applications you can see.')
  })

  it('cannot share while available agents are loading and offers selection when loading completes', async () => {
    const load = deferred<ReturnType<typeof ok<Schemas['AvailableAgentResponseListResponse']>>>()
    calls.get.mockImplementation(async (path: string) => path === availableAgentsPath ? load.promise : getResponse(path))
    await mountPage()
    await openAgentSharing()
    expect(dialog().text()).toContain('Loading available agents')
    expect(dialog().get('[role="combobox"][aria-label="Agent"]').attributes('disabled')).toBeDefined()
    await submit()
    expect(calls.post).not.toHaveBeenCalled()
    load.resolve(ok({ Data: availableAgents, Total: availableAgents.length }))
    await settle()
    await chooseAgent('new')
    await submit()
    expect(calls.post).toHaveBeenCalledOnce()
  })

  it('cannot share after available agents fail to load and allows retry', async () => {
    calls.get.mockImplementation(async (path: string) => path === availableAgentsPath ? rejected('Agents unavailable.') : getResponse(path))
    await mountPage()
    await openAgentSharing()
    expect(dialog().text()).toContain('Agents unavailable.')
    expect(dialog().get('[role="combobox"][aria-label="Agent"]').attributes('disabled')).toBeDefined()
    await submit()
    expect(calls.post).not.toHaveBeenCalled()
    calls.get.mockImplementation(async (path: string) => getResponse(path))
    await button('Try again', dialog()).trigger('click')
    await settle()
    await chooseAgent('new')
    await submit()
    expect(calls.post).toHaveBeenCalledOnce()
  })

  it('explains where to create agents when none are available and prevents sharing', async () => {
    availableAgents = []
    await mountPage()
    await openAgentSharing()
    expect(dialog().text()).toContain('No agents available')
    expect(dialog().text()).toContain('Instance > Agents')
    expect(dialog().get('[role="combobox"][aria-label="Agent"]').attributes('disabled')).toBeDefined()
    await submit()
    expect(calls.post).not.toHaveBeenCalled()
  })

  it('keeps the person form separate and rejects a reserved agent address without a request', async () => {
    await mountPage()
    await button('Share').trigger('click')
    await settle()
    expect(dialog().find('[role="combobox"][aria-label="Agent"]').exists()).toBe(false)
    expect(dialog().find('fieldset').exists()).toBe(false)
    await dialog().get('input[type="email"]').setValue('  BuildBot@AGENT.LOCAL  ')
    await submit()
    expect(calls.post).not.toHaveBeenCalled()
    expect(dialog().get('[role="alert"]').text()).toBe('Use Share with agent to select an agent by name.')
    expect(dialog().text()).not.toMatch(/@agent\.local/i)
  })

  it('sends chosen read, write, delete, and rebuild grants when adding an agent', async () => {
    await mountPage()
    await startSharing()
    await choosePermission('security', 'None')
    await choosePermission('entities', 'Read and write')
    await deletionControl('reports').trigger('click')
    await choosePermission('rebuild', 'Allow')
    await submit()
    expect(submittedPolicy(calls.post)).toEqual(expect.arrayContaining([
      { Resource: 'security', Read: false, Write: false, Delete: false },
      { Resource: 'entities', Read: true, Write: true, Delete: false },
      { Resource: 'reports', Read: true, Write: false, Delete: true },
      { Resource: 'rebuild', Read: false, Write: true, Delete: false },
    ]))
    expect(submittedPolicy(calls.post)).toHaveLength(permissionResources.length)
  })

  it('omits agent permissions and clears errors for a person after a customized agent draft failed', async () => {
    await mountPage()
    await startSharing()
    await choosePermission('rebuild', 'Allow')
    calls.post.mockResolvedValueOnce(rejected('Choose valid agent rights.', 'Permissions'))
    await submit()
    expect(dialog().get('[role="alert"]').text()).toBe('Choose valid agent rights.')
    await button('Cancel', dialog()).trigger('click')
    await settle()
    await button('Share').trigger('click')
    await settle()
    await dialog().get('input[type="email"]').setValue('another@example.test')
    expect(dialog().find('[role="alert"]').exists()).toBe(false)
    expect(dialog().find('fieldset').exists()).toBe(false)
    expect(dialog().text()).toContain('The user gets full access')
    await submit()
    expect(submittedPolicy(calls.post)).toBeUndefined()
    const wireBody = JSON.parse(JSON.stringify(calls.post.mock.calls.at(-1)?.[1].body))
    expect(wireBody).toEqual({ Email: 'another@example.test' })
    expect(calls.warning).toHaveBeenCalledWith(expect.stringContaining('No email was sent'))
  })

  it('loads existing grants, saves edits, and reloads the server policy and collaborator summary', async () => {
    collaborators[0] = { ID: 17, Email: 'existing@agent.local', Permissions: [
      { Resource: 'entities', Read: true, Write: false, Delete: true },
    ] }
    await mountPage()
    await startEditing()
    expect(dialog().text()).toContain('existing')
    expect(dialog().text()).not.toContain('@agent.local')
    expect(permissionControl('entities').text()).toBe('Read')
    expect(deletionControl('entities').attributes('aria-checked')).toBe('true')
    expect(permissionControl('security').text()).toBe('None')
    await choosePermission('security', 'Read and write')
    await choosePermission('rebuild', 'Allow')
    await submit()
    expect(calls.put).toHaveBeenCalledOnce()
    expect(calls.put.mock.calls[0]?.[0]).toBe(editPath)
    expect(calls.put.mock.calls[0]?.[1].params).toEqual({ path: { appToken: 'shared-app', id: 17 } })
    expect(submittedPolicy(calls.put)).toEqual(expect.arrayContaining([
      { Resource: 'entities', Read: true, Write: false, Delete: true },
      { Resource: 'security', Read: true, Write: true, Delete: false },
      { Resource: 'rebuild', Read: false, Write: true, Delete: false },
    ]))
    expect(calls.get.mock.calls.filter(([path]) => path === collaboratorsPath)).toHaveLength(2)
    expect(calls.success).toHaveBeenCalledWith('Updated the rights of existing.')
    expect(body().find('[role="dialog"]').exists()).toBe(false)
    expect(body().text()).toContain('Read 2 areas · write 2 · delete 1 · rebuild allowed')
    await startEditing()
    expect(permissionControl('security').text()).toBe('Read and write')
    expect(permissionControl('rebuild').text()).toBe('Allow')
  })

  it('cancels edits without mutating the saved policy and resets a reopened draft', async () => {
    const original = structuredClone(collaborators[0])
    await mountPage()
    await startEditing()
    await choosePermission('security', 'Read and write')
    await deletionControl('entities').trigger('click')
    await button('Cancel', dialog()).trigger('click')
    await settle()
    expect(calls.put).not.toHaveBeenCalled()
    expect(collaborators[0]).toEqual(original)
    await startEditing()
    expect(permissionControl('security').text()).toBe('Read')
    expect(deletionControl('entities').attributes('aria-checked')).toBe('false')
  })

  it('resets a cancelled new-agent draft to read-only the next time Share opens', async () => {
    await mountPage()
    await startSharing()
    await choosePermission('security', 'Read and write')
    await choosePermission('rebuild', 'Allow')
    await button('Cancel', dialog()).trigger('click')
    await settle()
    expect(calls.post).not.toHaveBeenCalled()
    await startSharing()
    expect(permissionControl('security').text()).toBe('Read')
    expect(permissionControl('rebuild').text()).toBe('None')
    await submit()
    expect(submittedPolicy(calls.post)).toEqual(readOnlyPolicy())
  })

  it.each([['sharing', 'post'], ['editing', 'put']] as const)('preserves the %s draft and renders permission errors for retry', async (action, method) => {
    await mountPage()
    if (action === 'sharing') {
      await startSharing()
    } else {
      await startEditing()
    }
    await choosePermission('security', 'Read and write')
    calls[method].mockResolvedValueOnce(rejected('Choose valid agent rights.', 'Permissions'))
    await submit()
    expect(dialog().get('[role="alert"]').text()).toBe('Choose valid agent rights.')
    expect(permissionControl('security').text()).toBe('Read and write')
    expect(calls.success).not.toHaveBeenCalled()
    await submit()
    expect(calls[method]).toHaveBeenCalledTimes(2)
    expect(submittedPolicy(calls[method])).toEqual(expect.arrayContaining([
      { Resource: 'security', Read: true, Write: true, Delete: false },
    ]))
    expect(body().find('[role="dialog"]').exists()).toBe(false)
  })

  it('preserves the edit draft after a network error', async () => {
    await mountPage()
    await startEditing()
    await choosePermission('entities', 'None')
    calls.put.mockRejectedValueOnce(new TypeError('Failed to fetch'))
    await submit()
    expect(dialog().get('[role="alert"]').text()).toContain('The Portal could not be reached.')
    expect(permissionControl('entities').text()).toBe('None')
    expect(calls.success).not.toHaveBeenCalled()
  })

  it('prevents duplicate saves and dismissal while a rights update is pending', async () => {
    const save = deferred<ReturnType<typeof ok<Schemas['CollaboratorResponse']>>>()
    calls.put.mockReturnValueOnce(save.promise)
    await mountPage()
    await startEditing()
    await submit()
    expect(button('Save', dialog()).attributes('disabled')).toBeDefined()
    expect(button('Cancel', dialog()).attributes('disabled')).toBeDefined()
    await submit()
    await dialog().trigger('keydown', { key: 'Escape' })
    await settle()
    expect(calls.put).toHaveBeenCalledOnce()
    expect(body().find('[role="dialog"]').exists()).toBe(true)
    save.resolve(ok({ ID: 17, Email: 'existing@agent.local', Permissions: readOnlyPolicy() }))
    await settle()
    expect(body().find('[role="dialog"]').exists()).toBe(false)
  })

  it.each(['pending', 'failed'] as const)('shares with server read-only defaults while the catalogue is %s', async (state) => {
    const load = deferred<ReturnType<typeof ok<Schemas['ApplicationPermissionsResponse']>>>()
    calls.get.mockImplementation(async (path: string) => path === permissionsPath
      ? state === 'pending' ? load.promise : rejected('Catalogue unavailable.')
      : getResponse(path))
    await mountPage()
    await startSharing()
    expect(dialog().find('fieldset').exists()).toBe(false)
    expect(dialog().text()).toContain(state === 'pending' ? 'Loading agent rights' : 'default read-only access')
    await submit()
    expect(calls.post).toHaveBeenCalledOnce()
    expect(submittedPolicy(calls.post)).toBeUndefined()
    expect(calls.success).toHaveBeenCalledWith(expect.stringContaining('rights later'))
    load.resolve(ok(catalogue()))
    await settle()
  })

  it('initializes an open share dialog when the delayed catalogue arrives', async () => {
    const load = deferred<ReturnType<typeof ok<Schemas['ApplicationPermissionsResponse']>>>()
    calls.get.mockImplementation(async (path: string) => path === permissionsPath ? load.promise : getResponse(path))
    await mountPage()
    await startSharing()
    load.resolve(ok(catalogue()))
    await settle()
    expect(permissionControl('security').text()).toBe('Read')
    expect(permissionControl('rebuild').text()).toBe('None')
    await submit()
    expect(submittedPolicy(calls.post)).toEqual(readOnlyPolicy())
  })

  it.each(['pending', 'failed'] as const)('does not overwrite existing rights while the catalogue is %s and restores the draft on recovery', async (state) => {
    const load = deferred<ReturnType<typeof ok<Schemas['ApplicationPermissionsResponse']>>>()
    const original = [{ Resource: 'security', Read: true, Write: true, Delete: false }]
    collaborators[0] = { ID: 17, Email: 'existing@agent.local', Permissions: original }
    calls.get.mockImplementation(async (path: string) => path === permissionsPath
      ? state === 'pending' ? load.promise : rejected('Catalogue unavailable.')
      : getResponse(path))
    await mountPage()
    await startEditing()
    await submit()
    expect(calls.put).not.toHaveBeenCalled()
    expect(dialog().find('fieldset').exists()).toBe(false)
    expect(collaborators[0]?.Permissions).toEqual(original)
    if (state === 'pending') {
      load.resolve(ok(catalogue()))
    } else {
      calls.get.mockImplementation(async (path: string) => getResponse(path))
      await button('Try again', dialog()).trigger('click')
    }
    await settle()
    expect(permissionControl('security').text()).toBe('Read and write')
    expect(permissionControl('entities').text()).toBe('None')
    await submit()
    expect(submittedPolicy(calls.put)).toEqual(expect.arrayContaining(original))
    expect(submittedPolicy(calls.put)?.filter((grant) => grant.Resource !== 'security').every((grant) => !grant.Read && !grant.Write && !grant.Delete)).toBe(true)
  })

  it('offers Edit rights only for agents', async () => {
    await mountPage()
    const editButtons = body().findAll('button').filter((candidate) => candidate.text().startsWith('Edit rights'))
    expect(editButtons.map((candidate) => candidate.text())).toEqual(['Edit rights of existing'])
    expect(body().text()).toContain('existing')
    expect(body().text()).not.toContain('@agent.local')
    expect(body().text()).toContain('Full access, except sharing')
  })

  it('uses the agent name in removal confirmation and toast while removing by collaborator ID', async () => {
    await mountPage()
    await button('Stop sharing with existing').trigger('click')
    await settle()
    const confirmation = new DOMWrapper(body().get('[role="alertdialog"]').element)
    expect(confirmation.text()).toContain('Stop sharing with existing?')
    expect(confirmation.text()).not.toContain('@agent.local')
    await button('Stop sharing', confirmation).trigger('click')
    await settle()
    expect(calls.remove).toHaveBeenCalledWith('/api/v1/applications/{appToken}/collaborators/{id}', {
      params: { path: { appToken: 'shared-app', id: 17 } },
    })
    expect(calls.success).toHaveBeenCalledWith('Stopped sharing with existing.')
    expect(body().find('[role="alertdialog"]').exists()).toBe(false)
    expect(body().text()).not.toContain('existing')
    expect(body().text()).toContain('person@example.test')
  })
})
