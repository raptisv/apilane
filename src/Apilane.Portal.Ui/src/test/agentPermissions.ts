import type { AgentPermissionGrant, AgentPermissionResource } from '@/lib/agentPermissions'

// Deliberately spell out the API's capabilities instead of deriving expected policies from
// the production helper: a regression in the default-grant logic must fail the component tests.
export const permissionResources: AgentPermissionResource[] = [
  { Resource: 'application', Name: 'Application settings', Description: 'Change application settings.', CanRead: false, CanWrite: true, CanDelete: false },
  { Resource: 'entities', Name: 'Entities', Description: 'Manage entities and properties.', CanRead: true, CanWrite: true, CanDelete: true },
  { Resource: 'security', Name: 'Security', Description: 'Manage access rules.', CanRead: true, CanWrite: true, CanDelete: false },
  { Resource: 'custom-endpoints', Name: 'Custom endpoints', Description: 'Manage custom SQL endpoints.', CanRead: true, CanWrite: true, CanDelete: true },
  { Resource: 'reports', Name: 'Reports', Description: 'Manage reports.', CanRead: true, CanWrite: true, CanDelete: true },
  { Resource: 'email-settings', Name: 'Email settings', Description: 'Manage email templates.', CanRead: true, CanWrite: true, CanDelete: false },
  { Resource: 'audit-log', Name: 'Audit log', Description: 'Read application history.', CanRead: true, CanWrite: false, CanDelete: false },
  { Resource: 'schema', Name: 'Schema', Description: 'Compare and import schemas.', CanRead: true, CanWrite: true, CanDelete: false },
  { Resource: 'rebuild', Name: 'Rebuild', Description: 'Remove all application data.', CanRead: false, CanWrite: true, CanDelete: false },
]

export function readOnlyPolicy(): AgentPermissionGrant[] {
  return [
    { Resource: 'application', Read: false, Write: false, Delete: false },
    { Resource: 'entities', Read: true, Write: false, Delete: false },
    { Resource: 'security', Read: true, Write: false, Delete: false },
    { Resource: 'custom-endpoints', Read: true, Write: false, Delete: false },
    { Resource: 'reports', Read: true, Write: false, Delete: false },
    { Resource: 'email-settings', Read: true, Write: false, Delete: false },
    { Resource: 'audit-log', Read: true, Write: false, Delete: false },
    { Resource: 'schema', Read: true, Write: false, Delete: false },
    { Resource: 'rebuild', Read: false, Write: false, Delete: false },
  ]
}
