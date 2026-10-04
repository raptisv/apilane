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
