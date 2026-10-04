import { ApiError } from './api'
import type { FormErrors } from './forms'
import type { Property } from './properties'
import type { Entity } from './entities'

/**
 * The rules of the data browser (EntityDataBrowser): which records an entity offers to do what,
 * the sort and filter sent to the API server, how values are shown and edited, the body of a save
 * and the history of a record. A copy of what the classic page (Views/Entity/Data.cshtml) does,
 * as pure functions, so they can be unit-tested (records.test.ts).
 *
 * Records, files and history live on the API server: the browser calls it through apiServer
 * (lib/apiServer.ts). Its answers are not in the Portal contract, so their shapes are described here.
 */

/**
 * What the data browser needs of an application: its token and API server, and the largest file it
 * takes. The ApplicationResponse of the Portal has all of it.
 */
export interface DataApplication {
  Token: string
  MaxAllowedFileSizeInKB: number
  Server: { ServerUrl: string }
}

/** One record as the API server sends it: property name to value. Dates are Unix milliseconds. */
export type DataRecord = Record<string, unknown>

/** A page of records: GET {ServerUrl}/api/Data/Get (or /api/Files/Get) with getTotal=true. */
export interface RecordPage {
  Data: DataRecord[]
  Total: number
}

/** One history entry: GET {ServerUrl}/api/EntityHistory/Get. `Data` is the record before the change, as JSON text. */
export interface HistoryEntry {
  ID: number
  RecordID: number
  Owner: number | null
  Created: number
  Data: string
}

/** The system entity of uploaded files: its records are read and written through the Files endpoints. */
export function isFiles(entity: Pick<Entity, 'Name'>): boolean {
  return entity.Name === 'Files'
}

/** The system entity of the application's users: a new one registers (Account/Register). */
export function isUsers(entity: Pick<Entity, 'Name'>): boolean {
  return entity.Name === 'Users'
}

/** What the data browser offers for an entity, from its flags (Data.cshtml:45, 477-495). */
export interface RecordActions {
  /** The 'New record', 'Register user' or 'Upload' button. */
  create: boolean
  createLabel: string
  edit: boolean
  delete: boolean
  download: boolean
  history: boolean
}

export function recordActions(entity: Entity): RecordActions {
  const files = isFiles(entity)
  const users = isUsers(entity)

  return {
    create: users || entity.AllowPost,
    createLabel: files ? 'Upload' : users ? 'Register user' : 'New record',
    edit: entity.AllowPut && !files,
    delete: entity.AllowDelete,
    download: files,
    history: !files && entity.RequireChangeTracking,
  }
}

/** The name of the primary key property: 'ID' for every entity today. */
export function primaryKey(properties: readonly Property[]): string {
  return properties.find((property) => property.IsPrimaryKey)?.Name ?? 'ID'
}

// Paging --------------------------------------------------------------------------------------

/** The page sizes the grid offers. The first one is the default, which the address leaves out. */
export const pageSizes: readonly number[] = [15, 30, 50, 100, 1000]

/** The page size of `?pageSize=`. Anything that is not one of pageSizes is the default. */
export function pageSizeFrom(value: unknown): number {
  const size = Number(Array.isArray(value) ? value[0] : value)

  return pageSizes.includes(size) ? size : (pageSizes[0] ?? 15)
}

/** The number of pages for `total` records: at least 1, so an empty grid is 'page 1 of 1'. */
export function pageCount(total: number, pageSize: number): number {
  return Math.max(1, Math.ceil(total / pageSize))
}

// Sorting --------------------------------------------------------------------------------------

/** The sort of the grid: one column at most, as in the classic page. */
export interface Sort {
  Property: string
  Direction: 'asc' | 'desc'
}

/**
 * The sort of `?sort=`: 'Name' sorts by Name ascending, '-Name' descending. A property the entity
 * does not have (names are case-sensitive) means no sort, and the API server then uses the entity's
 * default order.
 */
export function sortFrom(value: unknown, properties: readonly Property[]): Sort | undefined {
  const text = String(Array.isArray(value) ? value[0] : (value ?? ''))
  const descending = text.startsWith('-')
  const name = descending ? text.slice(1) : text

  return properties.some((property) => property.Name === name)
    ? { Property: name, Direction: descending ? 'desc' : 'asc' }
    : undefined
}

/** The `?sort=` value of a sort; undefined removes it from the address. */
export function sortQuery(sort: Sort | undefined): string | undefined {
  return sort ? `${sort.Direction === 'desc' ? '-' : ''}${sort.Property}` : undefined
}

/** A click on a column header: ascending, then descending, then no sort. Another column starts at ascending. */
export function nextSort(current: Sort | undefined, property: string): Sort | undefined {
  if (current?.Property !== property) {
    return { Property: property, Direction: 'asc' }
  }

  return current.Direction === 'asc' ? { Property: property, Direction: 'desc' } : undefined
}

/** The `sort` parameter of Data/Get: JSON [{ Property, Direction }], or undefined for none. */
export function sortParam(sort: Sort | undefined): string | undefined {
  return sort ? JSON.stringify([sort]) : undefined
}

// Filtering ------------------------------------------------------------------------------------

export type FilterOperator = 'contains' | 'equal'

/** What one filter box of the grid holds. `value` '' is no filter; a Boolean box holds 'true' or 'false'. */
export interface ColumnFilter {
  operator: FilterOperator
  value: string | number
}

/**
 * The operators a column's filter offers, the default first (Data.cshtml:87-101): a String
 * 'contains' or 'equal', an encrypted String 'equal' only (its stored value is encrypted, so only a
 * whole value can be found), a Number, Date or Boolean 'equal'.
 */
export function filterOperators(property: Property): FilterOperator[] {
  return property.Type === 'String' && !property.Encrypted ? ['contains', 'equal'] : ['equal']
}

/** An empty filter box for every property. */
export function emptyFilters(properties: readonly Property[]): Record<string, ColumnFilter> {
  return Object.fromEntries(
    properties.map((property) => [property.Name, { operator: filterOperators(property)[0] ?? 'equal', value: '' }]),
  )
}

/** Whether any filter box holds a value. */
export function hasFilters(filters: Record<string, ColumnFilter>): boolean {
  return Object.values(filters).some((filter) => String(filter.value).trim() !== '')
}

/**
 * The `filter` parameter of Data/Get: { Logic: 'and', Filters: [{ Property, Operator, Value }] } as
 * JSON, one entry per box with a value, or undefined when there is none. A Boolean is sent as '1' or
 * '0', the way the classic page does.
 */
export function filterParam(properties: readonly Property[], filters: Record<string, ColumnFilter>): string | undefined {
  const items: { Property: string; Operator: FilterOperator; Value: string }[] = []

  for (const property of properties) {
    const filter = filters[property.Name]
    const value = filter ? String(filter.value).trim() : ''

    if (!filter || value === '') {
      continue
    }

    const operator = filterOperators(property).includes(filter.operator) ? filter.operator : 'equal'

    items.push({
      Property: property.Name,
      Operator: operator,
      Value: property.Type === 'Boolean' ? (value === 'true' ? '1' : '0') : value,
    })
  }

  return items.length > 0 ? JSON.stringify({ Logic: 'and', Filters: items }) : undefined
}

// Values ---------------------------------------------------------------------------------------

/** The text format of a date in the grid and the form: 'yyyy-MM-dd HH:mm:ss.SSS'. */
export const dateFormat = 'yyyy-MM-dd HH:mm:ss.SSS'

/** A date as 'yyyy-MM-dd HH:mm:ss.SSS', in the browser's local time or in UTC. */
export function formatRecordDate(value: number | Date, local: boolean): string {
  const date = value instanceof Date ? value : new Date(value)
  const pad = (n: number, width = 2) => String(n).padStart(width, '0')

  const parts = local
    ? [date.getFullYear(), date.getMonth() + 1, date.getDate(), date.getHours(), date.getMinutes(), date.getSeconds(), date.getMilliseconds()]
    : [
        date.getUTCFullYear(),
        date.getUTCMonth() + 1,
        date.getUTCDate(),
        date.getUTCHours(),
        date.getUTCMinutes(),
        date.getUTCSeconds(),
        date.getUTCMilliseconds(),
      ]

  const [year, month, day, hours, minutes, seconds, ms] = parts.map(Number) as [number, number, number, number, number, number, number]

  return `${pad(year, 4)}-${pad(month)}-${pad(day)} ${pad(hours)}:${pad(minutes)}:${pad(seconds)}.${pad(ms, 3)}`
}

/**
 * The text of a value in the grid and the history, or null for a null value (the grid shows a
 * 'null' marker). Dates follow Data.cshtml:507: the system UTC dates (Created, LastLogin: IsUtc) in
 * the browser's local time, every other date as stored, in UTC.
 */
export function cellText(property: Property, value: unknown): string | null {
  if (value === null || value === undefined) {
    return null
  }

  if (property.Type === 'Date') {
    const ms = Number(value)

    return Number.isFinite(ms) ? formatRecordDate(ms, property.IsUtc) : String(value)
  }

  return typeof value === 'object' ? JSON.stringify(value) : String(value)
}

// The record form ------------------------------------------------------------------------------

/** What one box of the record form holds: text, a number box's number, or 'true' / 'false' / '' for a Boolean. */
export type FormValue = string | number
export type RecordForm = Record<string, FormValue>

/**
 * The properties the record form shows: those the data API lets a caller set (AllowEdit). A new
 * user is registered without Roles, which Account/Register does not take (Data.cshtml:859-862).
 */
export function formProperties(entity: Entity, creating: boolean): Property[] {
  return (entity.Properties ?? []).filter(
    (property) => property.AllowEdit && !(creating && isUsers(entity) && property.Name === 'Roles'),
  )
}

/**
 * The boxes of the record form, filled as the classic page fills them (Data.cshtml:922-940): for a
 * new record every date is now, in the browser's local time; for an edit every date is the stored
 * value in UTC. `now` is a parameter so the tests can fix it.
 */
export function initialForm(properties: readonly Property[], record: DataRecord | undefined, now: Date): RecordForm {
  const form: RecordForm = {}

  for (const property of properties) {
    const value = record?.[property.Name]

    if (property.Type === 'Date') {
      const ms = Number(value)
      form[property.Name] = record
        ? value === null || value === undefined || !Number.isFinite(ms) || ms <= 0
          ? ''
          : formatRecordDate(ms, false)
        : formatRecordDate(now, true)
    } else if (property.Type === 'Boolean') {
      form[property.Name] = value === null || value === undefined ? '' : value === true || value === 1 || value === '1' || value === 'true' ? 'true' : 'false'
    } else {
      form[property.Name] = value === null || value === undefined ? '' : typeof value === 'number' ? value : String(value)
    }
  }

  return form
}

/** One box as the API server takes it: an empty box is null, a Boolean '1' or '0', anything else its text. */
export function bodyValue(property: Property, value: FormValue | undefined): string | null {
  const text = value === undefined ? '' : String(value)

  if (text === '') {
    return null
  }

  if (property.Type === 'Boolean') {
    return text === 'true' ? '1' : '0'
  }

  return text
}

/** The body of a new record: every box of the form (Data.cshtml:779-802). */
export function createBody(properties: readonly Property[], form: RecordForm): Record<string, string | null> {
  return Object.fromEntries(properties.map((property) => [property.Name, bodyValue(property, form[property.Name])]))
}

/**
 * The body of an edit: the primary key and only the boxes the user changed, so a save does not
 * overwrite what someone else changed in the other fields meanwhile. Undefined when nothing changed.
 */
export function updateBody(
  properties: readonly Property[],
  initial: RecordForm,
  form: RecordForm,
  key: string,
  id: unknown,
): Record<string, unknown> | undefined {
  const body: Record<string, unknown> = {}

  for (const property of properties) {
    if (String(form[property.Name] ?? '') !== String(initial[property.Name] ?? '')) {
      body[property.Name] = bodyValue(property, form[property.Name])
    }
  }

  return Object.keys(body).length > 0 ? { [key]: id, ...body } : undefined
}

/**
 * formErrors (lib/forms.ts) for an error of the API server: its Property can name several
 * properties, comma-separated ('Email,Username' for a unique constraint of two columns), and each
 * of them gets the message.
 */
export function recordErrors(error: Error | undefined, fieldNames: readonly string[]): FormErrors {
  const fields: Record<string, string> = {}

  if (!error) {
    return { fields, message: undefined }
  }

  const names = error instanceof ApiError && error.property
    ? error.property.split(',').map((name) => name.trim()).filter((name) => fieldNames.includes(name))
    : []

  for (const name of names) {
    fields[name] = error.message
  }

  return { fields, message: names.length > 0 ? undefined : error.message }
}

// Files ----------------------------------------------------------------------------------------

/**
 * Whether a file is larger than the application allows. The classic page counts a KB as 1000
 * bytes (Data.cshtml:906); the API server still checks on its own.
 */
export function fileTooLarge(size: number, maxSizeInKB: number): boolean {
  return size > maxSizeInKB * 1000
}

// History --------------------------------------------------------------------------------------

/** How many history entries the panel shows: the newest, as the classic page (Data.cshtml:662). */
export const historyLimit = 100

export interface HistoryRow {
  id: number
  /** When the change was made, in the browser's local time (Data.cshtml:679). */
  timestamp: string
  owner: number | null
  /** The record before the change. */
  values: DataRecord
  /** The properties whose value differs from the entry below it (the one before). */
  changed: Set<string>
}

/**
 * The history entries of a record, newest first as the API server sends them, with the snapshot
 * read from its JSON and the properties that differ from the older entry under it marked. The
 * oldest entry has nothing to compare with, so nothing is marked in it.
 */
export function historyRows(entries: readonly HistoryEntry[], properties: readonly Property[]): HistoryRow[] {
  const snapshots = entries.map((entry) => parseSnapshot(entry.Data))

  return entries.map((entry, index) => {
    const values = snapshots[index] ?? {}
    const older = snapshots[index + 1]
    const changed = new Set<string>()

    if (older) {
      for (const property of properties) {
        if (JSON.stringify(values[property.Name] ?? null) !== JSON.stringify(older[property.Name] ?? null)) {
          changed.add(property.Name)
        }
      }
    }

    return { id: entry.ID, timestamp: formatRecordDate(entry.Created, true), owner: entry.Owner ?? null, values, changed }
  })
}

function parseSnapshot(data: string): DataRecord {
  try {
    const value: unknown = JSON.parse(data)

    return typeof value === 'object' && value !== null && !Array.isArray(value) ? (value as DataRecord) : {}
  } catch {
    // A snapshot that is not JSON shows as empty cells rather than breaking the panel.
    return {}
  }
}
