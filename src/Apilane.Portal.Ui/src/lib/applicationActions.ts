import { needsConnectionString } from './applications'

/**
 * The texts and rules of the application settings: the status change, rebuild and delete (asked
 * from the settings screen and from the cards on the applications page) and the edit form. Pure
 * functions and constants, so both places say the same thing and the rules can be unit-tested.
 */

/** What being offline means. */
export const offlineWarning = 'The application will not be available to the users, until the status is set back online.'

/** The box the user ticks before a rebuild or a delete. */
export const understandText = 'I understand that all associated data and existing functionality will be lost'

/** One line of the consequences of a rebuild or a delete. `strong` lines are emphasised. */
export interface Consequence {
  text: string
  strong?: boolean
}

/** What a rebuild does. */
export const rebuildConsequences: readonly Consequence[] = [
  { text: 'The application token will remain the same.' },
  { text: 'All entities and their properties will remain as is.' },
  { text: 'All data will be lost.', strong: true },
  { text: 'This action is not reversible!', strong: true },
]

/** What a delete does: the warning, then what goes with the application. */
export const deleteConsequences: readonly Consequence[] = [
  { text: 'The application will be deleted. This action is not reversible!', strong: true },
  {
    text: 'Its database and files on the API server are removed, and so are its entities, properties, custom endpoints, reports and sharing.',
  },
]

/** What the status button offers for an application that is `online` now, and the texts around it. */
export interface StatusChange {
  /** The value to send: the opposite of the current one. */
  Online: boolean
  /** The button. */
  label: string
  title: string
  description: string
  /** The toast after the change. */
  done: string
  /** Going offline is the harmful direction: its button is red. */
  destructive: boolean
}

export function statusChange(name: string, online: boolean): StatusChange {
  return online
    ? {
        Online: false,
        label: 'Take offline',
        title: `Take ${name} offline?`,
        description: offlineWarning,
        done: `${name} is offline.`,
        destructive: true,
      }
    : {
        Online: true,
        label: 'Bring online',
        title: `Bring ${name} online?`,
        description: 'The application will be available to the users again.',
        done: `${name} is online.`,
        destructive: false,
      }
}

/**
 * The ConnectionString of the edit request. The box starts empty because the API never sends the
 * stored value: empty sends null, which keeps it. SQLite has no connection string, so always null.
 */
export function connectionStringToSend(databaseType: string, typed: string): string | null {
  return needsConnectionString(databaseType) && typed !== '' ? typed : null
}
