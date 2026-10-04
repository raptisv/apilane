import { describe, expect, it } from 'vitest'
import { ApiError } from './api'
import type { Entity } from './entities'
import type { Property } from './properties'
import {
  cellText,
  createBody,
  emptyFilters,
  fileTooLarge,
  filterOperators,
  filterParam,
  formatRecordDate,
  formProperties,
  hasFilters,
  historyRows,
  initialForm,
  nextSort,
  pageCount,
  pageSizeFrom,
  primaryKey,
  recordActions,
  recordErrors,
  sortFrom,
  sortParam,
  sortQuery,
  updateBody,
} from './records'

function prop(Name: string, Type: string, extra: Partial<Property> = {}): Property {
  return {
    Name,
    Type,
    IsPrimaryKey: false,
    IsSystem: false,
    Position: 0,
    Required: false,
    Encrypted: false,
    AllowEdit: true,
    IsUtc: false,
    AllowMin: false,
    AllowMaxEdit: false,
    AllowValidationRegex: false,
    ...extra,
  }
}

const id = prop('ID', 'Number', { IsPrimaryKey: true, IsSystem: true, AllowEdit: false })
const name = prop('Name', 'String')
const secret = prop('Secret', 'String', { Encrypted: true })
const price = prop('Price', 'Number', { DecimalPlaces: 2 })
const paid = prop('Paid', 'Boolean')
const due = prop('Due', 'Date')
const created = prop('Created', 'Date', { IsSystem: true, AllowEdit: false, IsUtc: true })
const properties = [id, name, secret, price, paid, due, created]

function entity(Name: string, extra: Partial<Entity> = {}): Entity {
  return {
    Name,
    IsSystem: false,
    IsReadOnly: false,
    RequireChangeTracking: false,
    HasDifferentiationProperty: false,
    AllowPost: true,
    AllowPut: true,
    AllowDelete: true,
    AllowAddProperties: true,
    Constraints: [],
    Properties: properties,
    ...extra,
  }
}

describe('recordActions', () => {
  it('follows the flags of a custom entity', () => {
    expect(recordActions(entity('Orders', { RequireChangeTracking: true }))).toEqual({
      create: true,
      createLabel: 'New record',
      edit: true,
      delete: true,
      download: false,
      history: true,
    })
  })

  it('offers nothing but reading for a read-only entity', () => {
    const actions = recordActions(entity('Logs', { AllowPost: false, AllowPut: false, AllowDelete: false }))

    expect([actions.create, actions.edit, actions.delete, actions.history]).toEqual([false, false, false, false])
  })

  it('registers a user even though Users does not allow a plain create', () => {
    const actions = recordActions(entity('Users', { AllowPost: false }))

    expect(actions.create).toBe(true)
    expect(actions.createLabel).toBe('Register user')
  })

  it('uploads, downloads and deletes files, but never edits them or shows history', () => {
    const actions = recordActions(entity('Files', { AllowPut: true, RequireChangeTracking: true }))

    expect(actions).toMatchObject({ createLabel: 'Upload', edit: false, download: true, delete: true, history: false })
  })
})

describe('primaryKey', () => {
  it('is the property marked as the primary key', () => {
    expect(primaryKey([name, prop('Key', 'Number', { IsPrimaryKey: true })])).toBe('Key')
  })

  it('falls back to ID when no property is marked', () => {
    expect(primaryKey([name])).toBe('ID')
  })
})

describe('paging', () => {
  it('accepts only the offered page sizes', () => {
    expect(pageSizeFrom('50')).toBe(50)
    expect(pageSizeFrom('7')).toBe(15)
    expect(pageSizeFrom(undefined)).toBe(15)
    expect(pageSizeFrom(['100'])).toBe(100)
  })

  it('counts at least one page', () => {
    expect(pageCount(0, 15)).toBe(1)
    expect(pageCount(31, 15)).toBe(3)
  })
})

describe('sorting', () => {
  it('reads ascending and descending from the address', () => {
    expect(sortFrom('Name', properties)).toEqual({ Property: 'Name', Direction: 'asc' })
    expect(sortFrom('-Price', properties)).toEqual({ Property: 'Price', Direction: 'desc' })
  })

  it('ignores a property the entity does not have, case-sensitively', () => {
    expect(sortFrom('name', properties)).toBeUndefined()
    expect(sortFrom('', properties)).toBeUndefined()
    expect(sortFrom(undefined, properties)).toBeUndefined()
  })

  it('writes the address value back', () => {
    expect(sortQuery({ Property: 'Name', Direction: 'desc' })).toBe('-Name')
    expect(sortQuery(undefined)).toBeUndefined()
  })

  it('cycles ascending, descending, none', () => {
    const asc = nextSort(undefined, 'Name')
    const desc = nextSort(asc, 'Name')

    expect(asc).toEqual({ Property: 'Name', Direction: 'asc' })
    expect(desc).toEqual({ Property: 'Name', Direction: 'desc' })
    expect(nextSort(desc, 'Name')).toBeUndefined()
  })

  it('starts another column at ascending', () => {
    expect(nextSort({ Property: 'Name', Direction: 'desc' }, 'Price')).toEqual({ Property: 'Price', Direction: 'asc' })
  })

  it('sends the API server a JSON list', () => {
    expect(sortParam({ Property: 'Name', Direction: 'asc' })).toBe('[{"Property":"Name","Direction":"asc"}]')
    expect(sortParam(undefined)).toBeUndefined()
  })
})

describe('filtering', () => {
  it('offers contains and equal for strings, equal only for the rest', () => {
    expect(filterOperators(name)).toEqual(['contains', 'equal'])
    expect(filterOperators(secret)).toEqual(['equal'])
    expect(filterOperators(price)).toEqual(['equal'])
    expect(filterOperators(due)).toEqual(['equal'])
    expect(filterOperators(paid)).toEqual(['equal'])
  })

  it('starts every box empty with its default operator', () => {
    const filters = emptyFilters([name, secret])

    expect(filters).toEqual({ Name: { operator: 'contains', value: '' }, Secret: { operator: 'equal', value: '' } })
    expect(hasFilters(filters)).toBe(false)
    expect(filterParam(properties, filters)).toBeUndefined()
  })

  it('ands the boxes with a value and sends a Boolean as 1 or 0', () => {
    const filters = {
      ...emptyFilters(properties),
      Name: { operator: 'contains' as const, value: ' ann ' },
      Price: { operator: 'equal' as const, value: 12.5 },
      Paid: { operator: 'equal' as const, value: 'false' },
    }

    expect(hasFilters(filters)).toBe(true)
    expect(JSON.parse(filterParam(properties, filters) ?? '')).toEqual({
      Logic: 'and',
      Filters: [
        { Property: 'Name', Operator: 'contains', Value: 'ann' },
        { Property: 'Price', Operator: 'equal', Value: '12.5' },
        { Property: 'Paid', Operator: 'equal', Value: '0' },
      ],
    })
  })

  it('never sends contains for an encrypted string', () => {
    const filters = { Secret: { operator: 'contains' as const, value: 'x' } }

    expect(JSON.parse(filterParam(properties, filters) ?? '').Filters[0].Operator).toBe('equal')
  })
})

describe('dates', () => {
  // 2026-03-04 05:06:07.089 UTC
  const ms = Date.UTC(2026, 2, 4, 5, 6, 7, 89)

  it('formats in UTC', () => {
    expect(formatRecordDate(ms, false)).toBe('2026-03-04 05:06:07.089')
  })

  it('formats in local time', () => {
    const local = new Date(ms)
    const expected = `${local.getFullYear()}-${String(local.getMonth() + 1).padStart(2, '0')}-${String(local.getDate()).padStart(2, '0')} ${String(local.getHours()).padStart(2, '0')}:${String(local.getMinutes()).padStart(2, '0')}:07.089`

    expect(formatRecordDate(ms, true)).toBe(expected)
  })

  it('shows the system UTC dates in local time and every other date in UTC', () => {
    expect(cellText(due, ms)).toBe('2026-03-04 05:06:07.089')
    expect(cellText(created, ms)).toBe(formatRecordDate(ms, true))
  })
})

describe('cellText', () => {
  it('is null for a null value', () => {
    expect(cellText(name, null)).toBeNull()
    expect(cellText(paid, undefined)).toBeNull()
  })

  it('shows other values as text', () => {
    expect(cellText(paid, true)).toBe('true')
    expect(cellText(price, 12.5)).toBe('12.5')
    expect(cellText(name, '<b>')).toBe('<b>')
  })
})

describe('the record form', () => {
  const now = new Date(2026, 0, 2, 3, 4, 5, 6)

  it('shows the properties a caller may set, and no Roles when registering a user', () => {
    const roles = prop('Roles', 'String', { IsSystem: true })
    const users = entity('Users', { Properties: [id, prop('Email', 'String'), roles, created] })

    expect(formProperties(users, true).map((p) => p.Name)).toEqual(['Email'])
    expect(formProperties(users, false).map((p) => p.Name)).toEqual(['Email', 'Roles'])
    expect(formProperties(entity('Orders'), true).map((p) => p.Name)).toEqual(['Name', 'Secret', 'Price', 'Paid', 'Due'])
  })

  it('fills a new record with now in local time for every date', () => {
    expect(initialForm([name, paid, due], undefined, now)).toEqual({ Name: '', Paid: '', Due: '2026-01-02 03:04:05.006' })
  })

  it('fills an edit from the record, dates in UTC', () => {
    const record = { ID: 7, Name: 'Anna', Price: 3, Paid: false, Due: Date.UTC(2026, 4, 6, 7, 8, 9, 10) }

    expect(initialForm([name, price, paid, due], record, now)).toEqual({
      Name: 'Anna',
      Price: 3,
      Paid: 'false',
      Due: '2026-05-06 07:08:09.010',
    })
  })

  it('leaves the boxes of null values empty on an edit', () => {
    expect(initialForm([name, paid, due], { ID: 1, Name: null, Paid: null, Due: null }, now)).toEqual({ Name: '', Paid: '', Due: '' })
  })

  it('sends empty boxes as null and Booleans as 1 or 0', () => {
    expect(createBody([name, price, paid, due], { Name: '', Price: 4, Paid: 'true', Due: '2026-01-01' })).toEqual({
      Name: null,
      Price: '4',
      Paid: '1',
      Due: '2026-01-01',
    })
  })

  it('sends only the changed boxes of an edit, with the primary key', () => {
    const initial = { Name: 'Anna', Price: 3, Paid: 'false' }

    expect(updateBody([name, price, paid], initial, { Name: 'Anna', Price: 4, Paid: '' }, 'ID', 7)).toEqual({
      ID: 7,
      Price: '4',
      Paid: null,
    })
  })

  it('has nothing to send when nothing changed', () => {
    const initial = { Name: 'Anna', Price: 3 }

    expect(updateBody([name, price], initial, { Name: 'Anna', Price: 3 }, 'ID', 7)).toBeUndefined()
  })
})

describe('recordErrors', () => {
  it('puts the message under every property the API server names', () => {
    const error = new ApiError(400, { Code: 'UNIQUE_CONSTRAINT_VIOLATION', Message: 'Value already exists', Property: 'Name, Price' })

    expect(recordErrors(error, ['Name', 'Price', 'Paid'])).toEqual({
      fields: { Name: 'Value already exists', Price: 'Value already exists' },
      message: undefined,
    })
  })

  it('shows the message above the form when no field of the form is named', () => {
    const error = new ApiError(400, { Code: 'REQUIRED', Message: 'Required', Property: 'Owner' })

    expect(recordErrors(error, ['Name'])).toEqual({ fields: {}, message: 'Required' })
    expect(recordErrors(new Error('Offline'), ['Name'])).toEqual({ fields: {}, message: 'Offline' })
    expect(recordErrors(undefined, ['Name'])).toEqual({ fields: {}, message: undefined })
  })
})

describe('fileTooLarge', () => {
  it('counts a KB as 1000 bytes, as the classic page', () => {
    expect(fileTooLarge(5000, 5)).toBe(false)
    expect(fileTooLarge(5001, 5)).toBe(true)
  })
})

describe('historyRows', () => {
  it('reads each snapshot and marks what differs from the entry before it', () => {
    const rows = historyRows(
      [
        { ID: 3, RecordID: 1, Owner: 5, Created: Date.UTC(2026, 0, 3), Data: '{"Name":"C","Price":2}' },
        { ID: 2, RecordID: 1, Owner: null, Created: Date.UTC(2026, 0, 2), Data: '{"Name":"B","Price":2}' },
        { ID: 1, RecordID: 1, Owner: 5, Created: Date.UTC(2026, 0, 1), Data: 'not json' },
      ],
      [name, price],
    )

    expect(rows.map((row) => [...row.changed])).toEqual([['Name'], ['Name', 'Price'], []])
    expect(rows[0]?.values).toEqual({ Name: 'C', Price: 2 })
    expect(rows[2]?.values).toEqual({})
    expect(rows[1]?.owner).toBeNull()
    expect(rows[0]?.timestamp).toBe(formatRecordDate(Date.UTC(2026, 0, 3), true))
  })
})
