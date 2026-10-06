import type { Schemas } from './api'

export type AgentPermissionGrant = Schemas['AgentPermissionGrant']
export type AgentPermissionResource = Schemas['AgentPermissionResource']
export type AgentAccessLevel = 'none' | 'read' | 'write'

/** New collaborators may read supported areas; destructive actions always need an explicit grant. */
export function readOnlyAgentPermissions(resources: AgentPermissionResource[]): AgentPermissionGrant[] {
  return resources.map((resource) => ({ Resource: resource.Resource, Read: resource.CanRead, Write: false, Delete: false }))
}

/** Copy every catalogue area into an editable form; absent grants stay denied. */
export function copyAgentPermissions(
  resources: AgentPermissionResource[],
  grants: AgentPermissionGrant[],
): AgentPermissionGrant[] {
  return resources.map((resource) => {
    const grant = grants.find((item) => item.Resource === resource.Resource)
    return {
      Resource: resource.Resource,
      Read: resource.CanRead && (grant?.Read ?? false),
      Write: resource.CanWrite && (grant?.Write ?? false),
      Delete: resource.CanDelete && (grant?.Delete ?? false),
    }
  })
}

export function agentAccessLevel(grant: AgentPermissionGrant | undefined): AgentAccessLevel {
  return grant?.Write ? 'write' : grant?.Read ? 'read' : 'none'
}

/** Write includes read when supported; removing read also removes a dependent delete grant. */
export function setAgentAccessLevel(
  grant: AgentPermissionGrant,
  resource: AgentPermissionResource,
  level: AgentAccessLevel,
): AgentPermissionGrant {
  const read = resource.CanRead && level !== 'none'
  return {
    Resource: resource.Resource,
    Read: read,
    Write: resource.CanWrite && level === 'write',
    Delete: resource.CanDelete && read && grant.Delete,
  }
}

export function agentPermissionsSummary(grants: AgentPermissionGrant[] | null | undefined): string {
  if (!grants) {
    return 'Read-only access'
  }

  const reads = grants.filter((grant) => grant.Read).length
  const writes = grants.filter((grant) => grant.Write).length
  const deletes = grants.filter((grant) => grant.Delete).length

  if (!reads && !writes && !deletes) {
    return 'Application summary only'
  }
  if (!writes && !deletes) {
    return `Read-only · ${reads} ${reads === 1 ? 'area' : 'areas'}`
  }

  const summary = [`Read ${reads} ${reads === 1 ? 'area' : 'areas'}`]

  if (writes) {
    summary.push(`write ${writes}`)
  }
  if (deletes) {
    summary.push(`delete ${deletes}`)
  }
  if (grants.some((grant) => grant.Resource === 'rebuild' && grant.Write)) {
    summary.push('rebuild allowed')
  }
  return summary.join(' · ')
}
