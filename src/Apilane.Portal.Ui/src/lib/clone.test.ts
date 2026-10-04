import { describe, expect, it } from 'vitest'
import {
  cloneableEntities,
  cloneCounters,
  clonePercent,
  currentEntities,
  entitiesToSend,
  etaText,
  failureText,
  formatEta,
  isFinished,
  phaseText,
} from './clone'
import type { CloneOperation } from './clone'

function operation(values: Partial<CloneOperation> = {}): CloneOperation {
  return {
    OperationId: 'op1',
    Status: 'Pending',
    ErrorMessage: null,
    OverallPercentage: 0,
    TotalEntitiesToCreate: 0,
    EntitiesCreated: 0,
    CurrentEntityCreatingName: null,
    TotalEntitiesToCloneData: 0,
    EntitiesDataCloned: 0,
    CurrentEntityCloningDataName: null,
    CurrentEntityTotalRecords: 0,
    CurrentEntityImportedRecords: 0,
    TotalRecordsAllEntities: 0,
    TotalRecordsImported: 0,
    EstimatedRemainingSeconds: null,
    StartedAtUtc: '2026-10-03T10:00:00Z',
    CompletedAtUtc: null,
    ClonedApplicationToken: 'clone-token',
    ...values,
  }
}

describe('cloneableEntities', () => {
  it('lists every entity but Files, system entities included, in the given order', () => {
    expect(cloneableEntities(['Users', 'Files', 'Orders', 'Customers'])).toEqual(['Users', 'Orders', 'Customers'])
  })

  it('matches Files exactly', () => {
    expect(cloneableEntities(['files', 'FilesArchive'])).toEqual(['files', 'FilesArchive'])
  })
})

describe('entitiesToSend', () => {
  it('sends the ticked names when records are copied', () => {
    expect(entitiesToSend(true, ['Users', 'Orders'])).toEqual(['Users', 'Orders'])
  })

  it('sends null when only the schema is cloned, whatever is ticked', () => {
    expect(entitiesToSend(false, ['Users', 'Orders'])).toBeNull()
  })
})

describe('isFinished', () => {
  it('is true for Completed and Failed only', () => {
    expect(isFinished(operation({ Status: 'Completed' }))).toBe(true)
    expect(isFinished(operation({ Status: 'Failed' }))).toBe(true)

    for (const Status of ['Pending', 'CreatingApplication', 'CreatingEntities', 'CloningData']) {
      expect(isFinished(operation({ Status }))).toBe(false)
    }
  })
})

describe('phaseText', () => {
  it('names each phase', () => {
    expect(phaseText('Pending')).toBe('Preparing clone operation…')
    expect(phaseText('CreatingApplication')).toBe('Creating application…')
    expect(phaseText('CreatingEntities')).toBe('Creating entities…')
    expect(phaseText('CloningData')).toBe('Cloning data…')
    expect(phaseText('Completed')).toBe('Completed')
    expect(phaseText('Failed')).toBe('Failed')
  })

  it('shows an unknown status as it is', () => {
    expect(phaseText('Paused')).toBe('Paused')
  })
})

describe('clonePercent', () => {
  it('keeps the percentage between 0 and 100', () => {
    expect(clonePercent(operation({ OverallPercentage: 42 }))).toBe(42)
    expect(clonePercent(operation({ OverallPercentage: -5 }))).toBe(0)
    expect(clonePercent(operation({ OverallPercentage: 140 }))).toBe(100)
  })
})

describe('cloneCounters', () => {
  it('shows the entities created while the schema is built', () => {
    for (const Status of ['Pending', 'CreatingApplication', 'CreatingEntities']) {
      expect(cloneCounters(operation({ Status, EntitiesCreated: 3, TotalEntitiesToCreate: 12 }))).toEqual([
        { label: 'Entities created', value: '3 / 12' },
      ])
    }
  })

  it('shows the entities and records copied while the data is cloned, with thousands separators', () => {
    const counters = cloneCounters(
      operation({
        Status: 'CloningData',
        EntitiesCreated: 12,
        TotalEntitiesToCreate: 12,
        EntitiesDataCloned: 2,
        TotalEntitiesToCloneData: 5,
        TotalRecordsImported: 12500,
        TotalRecordsAllEntities: 1234567,
      }),
    )

    expect(counters).toEqual([
      { label: 'Entities cloned', value: '2 / 5' },
      { label: 'Records', value: '12,500 / 1,234,567' },
    ])
  })

  it('shows nothing for a finished operation', () => {
    expect(cloneCounters(operation({ Status: 'Completed', EntitiesCreated: 12, TotalEntitiesToCreate: 12 }))).toEqual([])
    expect(cloneCounters(operation({ Status: 'Failed' }))).toEqual([])
  })
})

describe('currentEntities', () => {
  it('is empty while no entity is being worked on', () => {
    expect(currentEntities(operation({ Status: 'CreatingApplication' }))).toEqual([])
  })

  it('names the entity being created', () => {
    expect(currentEntities(operation({ Status: 'CreatingEntities', CurrentEntityCreatingName: 'Orders' }))).toEqual([
      { label: 'Creating entity', name: 'Orders' },
    ])
  })

  it('names the entity whose records are copied, with its record counts', () => {
    const current = currentEntities(
      operation({
        Status: 'CloningData',
        CurrentEntityCloningDataName: 'Orders',
        CurrentEntityImportedRecords: 3000,
        CurrentEntityTotalRecords: 25000,
      }),
    )

    expect(current).toEqual([{ label: 'Cloning data', name: 'Orders', records: '3,000 / 25,000 records' }])
  })

  it('is empty for a finished operation, even when a name was left behind', () => {
    expect(currentEntities(operation({ Status: 'Failed', CurrentEntityCreatingName: 'Orders' }))).toEqual([])
  })
})

describe('formatEta', () => {
  it("says 'calculating…' while there is no estimate", () => {
    expect(formatEta(null)).toBe('calculating…')
    expect(formatEta(undefined)).toBe('calculating…')
    expect(formatEta(0)).toBe('calculating…')
    expect(formatEta(-3)).toBe('calculating…')
  })

  it('shows seconds under a minute, rounded up', () => {
    expect(formatEta(0.2)).toBe('1 second')
    expect(formatEta(12.1)).toBe('13 seconds')
    expect(formatEta(59)).toBe('59 seconds')
  })

  it('shows minutes and seconds under an hour', () => {
    expect(formatEta(60)).toBe('1m 0s')
    expect(formatEta(125)).toBe('2m 5s')
    expect(formatEta(3599)).toBe('59m 59s')
  })

  it("never shows 60 seconds as a part: 59.5 is '1m 0s' and 119.5 is '2m 0s'", () => {
    expect(formatEta(59.5)).toBe('1m 0s')
    expect(formatEta(119.5)).toBe('2m 0s')
  })

  it('shows hours and minutes from an hour up', () => {
    expect(formatEta(3600)).toBe('1h 0m')
    expect(formatEta(4500)).toBe('1h 15m')
    expect(formatEta(3599.5)).toBe('1h 0m')
    expect(formatEta(90000)).toBe('25h 0m')
  })
})

describe('etaText', () => {
  it('is shown once records are being copied and there is an estimate', () => {
    expect(etaText(operation({ Status: 'CloningData', TotalRecordsImported: 1000, EstimatedRemainingSeconds: 125 }))).toBe('2m 5s')
  })

  it('is hidden while there is no estimate', () => {
    expect(etaText(operation({ Status: 'CloningData', TotalRecordsImported: 1000, EstimatedRemainingSeconds: null }))).toBeUndefined()
  })

  it('is hidden before the first record is copied', () => {
    expect(etaText(operation({ Status: 'CloningData', TotalRecordsImported: 0, EstimatedRemainingSeconds: 10 }))).toBeUndefined()
  })

  it('is hidden for a finished operation, whose estimate is 0', () => {
    expect(etaText(operation({ Status: 'Completed', TotalRecordsImported: 1000, EstimatedRemainingSeconds: 0 }))).toBeUndefined()
    expect(etaText(operation({ Status: 'Failed', TotalRecordsImported: 1000, EstimatedRemainingSeconds: 0 }))).toBeUndefined()
  })
})

describe('failureText', () => {
  it('is the message of the operation', () => {
    expect(failureText(operation({ Status: 'Failed', ErrorMessage: 'Could not create application on Api server' }))).toBe(
      'Could not create application on Api server',
    )
  })

  it('is a general message when the operation has none', () => {
    expect(failureText(operation({ Status: 'Failed', ErrorMessage: null }))).toBe('An unexpected error occurred during cloning.')
    expect(failureText(operation({ Status: 'Failed', ErrorMessage: '' }))).toBe('An unexpected error occurred during cloning.')
  })
})
