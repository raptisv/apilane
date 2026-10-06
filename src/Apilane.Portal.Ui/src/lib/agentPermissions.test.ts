import { describe, expect, it } from 'vitest'
import { agentPermissionsSummary, copyAgentPermissions, readOnlyAgentPermissions, setAgentAccessLevel } from './agentPermissions'
import type { AgentPermissionGrant, AgentPermissionResource } from './agentPermissions'

const entities: AgentPermissionResource = {
  Resource: 'entities', Name: 'Entities', Description: '', CanRead: true, CanWrite: true, CanDelete: true,
}
const rebuild: AgentPermissionResource = {
  Resource: 'rebuild', Name: 'Rebuild', Description: '', CanRead: false, CanWrite: true, CanDelete: false,
}

describe('agent permission editing', () => {
  it('starts readable areas read-only, without enabling write-only or destructive operations', () => {
    expect(readOnlyAgentPermissions([entities, rebuild])).toEqual([
      { Resource: 'entities', Read: true, Write: false, Delete: false },
      { Resource: 'rebuild', Read: false, Write: false, Delete: false },
    ])
  })

  it('keeps absent grants denied and edits a copy of the saved policy', () => {
    const saved: AgentPermissionGrant = { Resource: 'entities', Read: true, Write: false, Delete: true }
    const copied = copyAgentPermissions([entities, rebuild], [saved])
    expect(copied).toEqual([
      saved,
      { Resource: 'rebuild', Read: false, Write: false, Delete: false },
    ])
    expect(copied[0]).not.toBe(saved)
  })

  it('preserves an independent deletion grant on read-only access, but clears it when read is removed', () => {
    const grant: AgentPermissionGrant = { Resource: 'entities', Read: true, Write: true, Delete: true }
    expect(setAgentAccessLevel(grant, entities, 'read')).toEqual({ Resource: 'entities', Read: true, Write: false, Delete: true })
    expect(setAgentAccessLevel(grant, entities, 'none')).toEqual({ Resource: 'entities', Read: false, Write: false, Delete: false })
  })

  it('does not invent read or delete rights for a write-only operation', () => {
    const grant: AgentPermissionGrant = { Resource: 'rebuild', Read: false, Write: false, Delete: false }
    expect(setAgentAccessLevel(grant, rebuild, 'write')).toEqual({ Resource: 'rebuild', Read: false, Write: true, Delete: false })
  })

  it('makes granted destructive operations visible in the collaborator summary', () => {
    expect(agentPermissionsSummary([
      { Resource: 'entities', Read: true, Write: false, Delete: true },
      { Resource: 'rebuild', Read: false, Write: true, Delete: false },
    ])).toBe('Read 1 area · write 1 · delete 1 · rebuild allowed')
  })
})
