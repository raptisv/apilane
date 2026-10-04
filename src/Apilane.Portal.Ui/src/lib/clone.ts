import type { Schemas } from './api'
import { formatCount } from './entities'

/**
 * The rules of the clone screens (the form and the progress page). Pure functions, no Vue, so
 * they can be unit-tested (clone.test.ts).
 */
export type CloneOperation = Schemas['CloneOperationResponse']

/** The entities the clone form lists: all but Files, whose records are never copied. */
export function cloneableEntities(names: readonly string[]): string[] {
  return names.filter((name) => name !== 'Files')
}

/**
 * The Entities of the clone request: the ticked names when records are copied, otherwise null.
 * An empty list would copy every entity, so the form does not send one (see CloneApplicationPage).
 */
export function entitiesToSend(cloneData: boolean, selected: readonly string[]): string[] | null {
  return cloneData ? [...selected] : null
}

/** Whether the operation has ended, well or badly: nothing changes any more, so polling stops. */
export function isFinished(operation: CloneOperation): boolean {
  return operation.Status === 'Completed' || operation.Status === 'Failed'
}

const phaseTexts: Record<string, string> = {
  Pending: 'Preparing clone operation…',
  CreatingApplication: 'Creating application…',
  CreatingEntities: 'Creating entities…',
  CloningData: 'Cloning data…',
  Completed: 'Completed',
  Failed: 'Failed',
}

/** What the operation is doing, in words. An unknown Status is shown as it is. */
export function phaseText(status: string): string {
  return phaseTexts[status] ?? status
}

/** The bar's value: OverallPercentage kept between 0 and 100. */
export function clonePercent(operation: CloneOperation): number {
  return Math.min(100, Math.max(0, Math.round(operation.OverallPercentage)))
}

export interface CloneCounter {
  label: string
  value: string
}

/**
 * The counters of the phase the operation is in: entities created while the schema is built,
 * entities and records copied while the data is. A finished operation has none.
 */
export function cloneCounters(operation: CloneOperation): CloneCounter[] {
  if (isFinished(operation)) {
    return []
  }

  if (operation.Status === 'CloningData') {
    return [
      { label: 'Entities cloned', value: `${operation.EntitiesDataCloned} / ${operation.TotalEntitiesToCloneData}` },
      {
        label: 'Records',
        value: `${formatCount(operation.TotalRecordsImported)} / ${formatCount(operation.TotalRecordsAllEntities)}`,
      },
    ]
  }

  return [{ label: 'Entities created', value: `${operation.EntitiesCreated} / ${operation.TotalEntitiesToCreate}` }]
}

export interface CloneCurrentEntity {
  /** 'Creating entity' or 'Cloning data'. */
  label: string
  name: string
  /** While records are copied: '1,000 / 25,000 records'. */
  records?: string
}

/** The entity being worked on right now: the one being created, the one whose records are copied. */
export function currentEntities(operation: CloneOperation): CloneCurrentEntity[] {
  const result: CloneCurrentEntity[] = []

  if (isFinished(operation)) {
    return result
  }

  if (operation.CurrentEntityCreatingName) {
    result.push({ label: 'Creating entity', name: operation.CurrentEntityCreatingName })
  }

  if (operation.CurrentEntityCloningDataName) {
    result.push({
      label: 'Cloning data',
      name: operation.CurrentEntityCloningDataName,
      records: `${formatCount(operation.CurrentEntityImportedRecords)} / ${formatCount(operation.CurrentEntityTotalRecords)} records`,
    })
  }

  return result
}

/** A remaining time in words: '45 seconds', '2m 5s', '1h 15m'; 'calculating…' when there is none yet. */
export function formatEta(seconds: number | null | undefined): string {
  if (seconds == null || seconds <= 0) {
    return 'calculating…'
  }

  // Rounded up first, so the parts never read '1m 60s'.
  const total = Math.ceil(seconds)

  if (total < 60) {
    return total === 1 ? '1 second' : `${total} seconds`
  }

  if (total < 3600) {
    return `${Math.floor(total / 60)}m ${total % 60}s`
  }

  return `${Math.floor(total / 3600)}h ${Math.floor((total % 3600) / 60)}m`
}

/**
 * The estimated time remaining, or undefined while there is nothing to estimate from: it is
 * shown only once records are being copied.
 */
export function etaText(operation: CloneOperation): string | undefined {
  if (isFinished(operation) || operation.EstimatedRemainingSeconds == null || operation.TotalRecordsImported <= 0) {
    return undefined
  }

  return formatEta(operation.EstimatedRemainingSeconds)
}

/** Why a clone failed: the message of the operation, or a general one when it has none. */
export function failureText(operation: CloneOperation): string {
  return operation.ErrorMessage || 'An unexpected error occurred during cloning.'
}
