import type { Schemas } from './api'

/**
 * How the applications list is arranged and labelled, for the applications page and the sidebar
 * switcher. Pure functions, no Vue, so they can be unit-tested (applications.test.ts).
 */
export type Application = Schemas['ApplicationResponse']
export type ApplicationServer = Schemas['ServerSummaryResponse']

export interface ServerGroup {
  server: ApplicationServer
  applications: Application[]
}

/**
 * Applications grouped by their API server. Servers are ordered by name; inside a server the
 * applications the user owns come first, then the shared ones, each ordered by name.
 */
export function groupByServer(applications: readonly Application[]): ServerGroup[] {
  const groups = new Map<number, ServerGroup>()

  for (const application of applications) {
    const group = groups.get(application.Server.ID)

    if (group) {
      group.applications.push(application)
    } else {
      groups.set(application.Server.ID, { server: application.Server, applications: [application] })
    }
  }

  const result = [...groups.values()].sort((a, b) => compareName(a.server.Name, b.server.Name) || a.server.ID - b.server.ID)

  for (const group of result) {
    group.applications.sort((a, b) => Number(b.IsOwner) - Number(a.IsOwner) || compareName(a.Name, b.Name))
  }

  return result
}

/** The applications whose name contains `search`, ignoring case. An empty search keeps them all. */
export function filterByName(applications: readonly Application[], search: string): Application[] {
  const text = search.trim().toLowerCase()

  return applications.filter((application) => application.Name.toLowerCase().includes(text))
}

// Ignores case first; names that differ only in case still get a fixed order.
function compareName(a: string, b: string): number {
  return a.localeCompare(b, 'en', { sensitivity: 'base' }) || a.localeCompare(b, 'en')
}

/** A section of an application: a tab of AppLayout. */
export interface ApplicationSection {
  label: string
  /**
   * The part of the address after the token (/apps/<token>/entities/...): the screens whose
   * address continues with it are in this section.
   */
  segment: string
  /** The route of the section's own screen, which takes the token only. */
  name: string
  /** Only the owner sees it (its route carries meta.requiresOwner). */
  ownerOnly?: boolean
}

/**
 * The sections of an application, in the order of the tabs. The route is `app-<segment>`, except
 * for Import: 'app-import' is the page that creates an application from an export.
 */
export const applicationSections: readonly ApplicationSection[] = [
  { label: 'Entities', segment: 'entities', name: 'app-entities' },
  { label: 'Data', segment: 'data', name: 'app-data' },
  { label: 'Security', segment: 'security', name: 'app-security' },
  { label: 'Custom endpoints', segment: 'endpoints', name: 'app-endpoints' },
  { label: 'Email', segment: 'email', name: 'app-email' },
  { label: 'Reports', segment: 'reports', name: 'app-reports' },
  { label: 'Sharing', segment: 'sharing', name: 'app-sharing', ownerOnly: true },
  { label: 'Import', segment: 'import', name: 'app-schema-import' },
  { label: 'Audit log', segment: 'audit-log', name: 'app-audit-log' },
  { label: 'Settings', segment: 'settings', name: 'app-settings' },
]

/**
 * The section an address of an application is in, or undefined for any other address (the clone
 * screens, a screen outside /apps/<token>). The letter case of the address does not matter.
 *
 *   sectionOf('/apps/<token>/entities/Orders/constraints') -> Entities
 *   sectionOf('/apps/<token>/clone')                        -> undefined
 */
export function sectionOf(path: string): ApplicationSection | undefined {
  const [, first, , segment] = path.toLowerCase().split('/')

  return first === 'apps' ? applicationSections.find((section) => section.segment === segment) : undefined
}

/**
 * Where the application switcher leads for one application: the section of the address the user
 * is on, so Security of one application opens Security of the other. Only the token goes along: an
 * entity, an endpoint, a report or ?item= belong to the application being left. Entities when the
 * address is in no section, and for Sharing of an application the user does not own.
 */
export function switcherTarget(
  path: string,
  application: Pick<Application, 'Token' | 'IsOwner'>,
): { name: string; params: { appToken: string } } {
  const section = sectionOf(path)
  const name = section && (!section.ownerOnly || application.IsOwner) ? section.name : 'app-entities'

  return { name, params: { appToken: application.Token } }
}

/** A size the API server reports in MB: '12.50 MB', or '1.50 GB' from 1024 MB up. */
export function formatStorage(megabytes: number): string {
  return megabytes >= 1024 ? `${(megabytes / 1024).toFixed(2)} GB` : `${megabytes.toFixed(2)} MB`
}

const databaseTypeLabels: Record<string, string> = {
  SQLLite: 'SQLite',
  SQLServer: 'SQL Server',
  MySQL: 'MySQL',
  PostgreSQL: 'PostgreSQL',
}

/** The DatabaseType values of the API, in the order a form offers them. */
export const databaseTypes: readonly string[] = Object.keys(databaseTypeLabels)

/** Whether the database type takes a connection string. SQLite does not: the API server creates the file itself. */
export function needsConnectionString(databaseType: string): boolean {
  return databaseType !== 'SQLLite'
}

/** The display name of a DatabaseType value of the API. An unknown value is shown as it is. */
export function databaseTypeLabel(databaseType: string): string {
  return databaseTypeLabels[databaseType] ?? databaseType
}
