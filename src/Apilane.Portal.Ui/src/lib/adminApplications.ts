import type { Schemas } from './api'
import { needsConnectionString } from './applications'

/**
 * The rules of the administrator's applications list (every application of the instance). Pure
 * functions, no Vue, so they can be unit-tested (adminApplications.test.ts).
 */
export type AdminApplication = Schemas['AdminApplicationResponse']

/**
 * The applications whose name, token or owner e-mail contains `search`, ignoring case. An empty
 * search keeps them all.
 */
export function searchApplications(applications: readonly AdminApplication[], search: string): AdminApplication[] {
  const text = search.trim().toLowerCase()

  return applications.filter((application) =>
    [application.Name, application.Token, application.OwnerEmail ?? ''].some((value) => value.toLowerCase().includes(text)),
  )
}

/** How many applications the list shows: '12 applications', or '3 of 12 applications' while a search hides some. */
export function countText(shown: number, total: number): string {
  const noun = total === 1 ? 'application' : 'applications'

  return shown === total ? `${total} ${noun}` : `${shown} of ${total} ${noun}`
}

/**
 * One line of an application's details. `stored` is set for a secret, which the API never sends:
 * only whether one is stored. Otherwise `value` is the text, or null when nothing is set.
 */
export interface ApplicationDetail {
  label: string
  value?: string | null
  stored?: boolean
}

/**
 * What the list shows when a row is opened: the settings the table itself has no column for, in
 * the order of the classic page.
 */
export function applicationDetails(application: AdminApplication): ApplicationDetail[] {
  return [
    { label: 'ID', value: String(application.ID) },
    // SQLite takes no connection string, so 'Not set' would read as a problem there.
    needsConnectionString(application.DatabaseType)
      ? { label: 'Connection string', stored: application.HasConnectionString }
      : { label: 'Connection string', value: 'Not needed' },
    { label: 'Maximum file size', value: `${application.MaxAllowedFileSizeInKB} KB` },
    { label: 'Auth token lifetime', value: `${application.AuthTokenExpireMinutes} minutes` },
    { label: 'Email confirmation redirect URL', value: emptyAsNull(application.EmailConfirmationRedirectUrl) },
    { label: 'Mail server', value: emptyAsNull(application.MailServer) },
    { label: 'Mail server port', value: application.MailServerPort == null ? null : String(application.MailServerPort) },
    { label: 'Mail sender address', value: emptyAsNull(application.MailFromAddress) },
    { label: 'Mail user name', value: emptyAsNull(application.MailUserName) },
    { label: 'Mail password', stored: application.HasMailPassword },
    { label: 'Mail sender display name', value: emptyAsNull(application.MailFromDisplayName) },
  ]
}

function emptyAsNull(value: string | null | undefined): string | null {
  return value == null || value.trim() === '' ? null : value
}
