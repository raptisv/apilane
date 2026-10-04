/**
 * Turns one changed property of an audit log entry into what the detail table shows. Every
 * display rule of the audit log is here; components/AuditLogDetail.vue uses it for the instance
 * and the application audit log.
 *
 * Pure functions only, no Vue: the rules can be unit-tested with stored Changes payloads.
 * Old and new values arrive as the raw stored strings. Items of a JSON array are compared by
 * their raw text, so the result depends on the stored text itself.
 */

/** The parts of an audit entry the rules need (Schemas['AuditChangeResponse'] fits). */
export interface AuditChange {
  Property: string
  OldValue?: string | null
  NewValue?: string | null
}

/** One security rule in the Added or Removed group. */
export interface SecurityItem {
  /** 'Name · RoleID · Action' */
  label: string
  record: string
  properties: string
  rateLimit: string
}

/** One line under an updated security rule. An empty side is not shown. */
export interface SecurityFieldDiff {
  field: string
  oldValue: string
  newValue: string
}

export interface SecurityUpdate {
  label: string
  fields: SecurityFieldDiff[]
}

/** What the value cells of one row show. */
export type ChangeView =
  /** Security + Modified: rules matched by Name|TypeID|RoleID|Action. All three empty means 'No changes'. */
  | { kind: 'security'; added: SecurityItem[]; updated: SecurityUpdate[]; removed: SecurityItem[] }
  /** Another JSON property + Modified: labels of the array items that left and that came. */
  | { kind: 'list'; removed: string[]; added: string[] }
  /** A JSON property + Created or Deleted: 'View JSON (n items)', or '(empty)' when count is 0. */
  | { kind: 'json'; count: number; json: string }
  /** Everything else: the stored text, one cell per value column. `old` cells are shown dimmed. */
  | { kind: 'text'; cells: { text: string; old: boolean }[] }

export const noDetailText = 'No detail available.'

const jsonProperties = ['security', 'entconstraints', 'entdefaultorder']

/** The column headers after 'Property'. They depend on the action alone. */
export function valueHeaders(action: string): string[] {
  if (action === 'Modified') {
    return ['Old Value', 'New Value']
  }

  return action === 'Created' ? ['Value'] : ['Previous Value']
}

/** 'View JSON (1 item)' / 'View JSON (3 items)'. */
export function jsonLinkText(count: number): string {
  return `View JSON (${count} ${count === 1 ? 'item' : 'items'})`
}

export function describeChange(action: string, change: AuditChange): ChangeView {
  const isJson = jsonProperties.includes(change.Property.toLowerCase())

  if (isJson && action === 'Modified') {
    const oldItems = parseJsonArray(change.OldValue)
    const newItems = parseJsonArray(change.NewValue)

    // The name is matched exactly here, although the list of JSON properties ignores case.
    return change.Property === 'Security' ? securityDiff(oldItems, newItems) : listDiff(oldItems, newItems)
  }

  if (isJson && (action === 'Created' || action === 'Deleted')) {
    const json = action === 'Created' ? change.NewValue : change.OldValue
    return { kind: 'json', count: parseJsonArray(json).length, json: formatJson(json) }
  }

  const oldCell = { text: change.OldValue ?? '<null>', old: true }
  const newCell = { text: change.NewValue ?? '<null>', old: false }

  if (action === 'Modified') {
    return { kind: 'text', cells: [oldCell, newCell] }
  }

  return { kind: 'text', cells: [action === 'Created' ? newCell : oldCell] }
}

// ---- JSON arrays ----

interface JsonItem {
  /** The item exactly as it is written in the stored text. */
  raw: string
  value: unknown
}

/** The items of a JSON array. Anything else (empty, not JSON, not an array) gives no items. */
function parseJsonArray(json: string | null | undefined): JsonItem[] {
  if (!json || json.trim() === '') {
    return []
  }

  let values: unknown

  try {
    values = JSON.parse(json)
  } catch {
    // Not JSON: such a value shows as an empty list.
    return []
  }

  if (!Array.isArray(values)) {
    return []
  }

  const raws = splitRawItems(json)

  // The text is valid JSON, so both have the same length; the fallback only guards that assumption.
  return values.map((value, i) => ({ value, raw: raws[i] ?? JSON.stringify(value) }))
}

/** Cuts the text of a valid JSON array into the text of its items. */
function splitRawItems(json: string): string[] {
  const items: string[] = []
  let depth = 0
  let inString = false
  let start = -1

  for (let i = 0; i < json.length; i++) {
    const c = json[i]

    if (inString) {
      if (c === '\\') {
        i++
      } else if (c === '"') {
        inString = false
      }
    } else if (c === '"') {
      inString = true
    } else if (c === '[' || c === '{') {
      depth++

      if (depth === 1) {
        start = i + 1
      }
    } else if (c === ']' || c === '}') {
      depth--

      if (depth === 0) {
        pushItem(items, json.slice(start, i))
      }
    } else if (c === ',' && depth === 1) {
      pushItem(items, json.slice(start, i))
      start = i + 1
    }
  }

  return items
}

function pushItem(items: string[], text: string): void {
  const trimmed = text.trim()

  if (trimmed !== '') {
    items.push(trimmed)
  }
}

function formatJson(json: string | null | undefined): string {
  if (!json || json.trim() === '') {
    return ''
  }

  try {
    return JSON.stringify(JSON.parse(json), null, 2)
  } catch {
    // Not JSON: show the stored text as it is.
    return json
  }
}

// ---- Reading item fields ----

function field(item: JsonItem, name: string): unknown {
  const value = item.value

  return typeof value === 'object' && value !== null ? (value as Record<string, unknown>)[name] : undefined
}

function textField(item: JsonItem, name: string): string {
  const value = field(item, name)

  return typeof value === 'string' ? value : ''
}

function intField(value: unknown, name: string): number {
  const number = typeof value === 'object' && value !== null ? (value as Record<string, unknown>)[name] : undefined

  return typeof number === 'number' ? number : 0
}

function rateLimitText(rateLimit: unknown): string {
  const window = intField(rateLimit, 'TimeWindowType')
  const label = window === 1 ? '/sec' : window === 2 ? '/min' : window === 3 ? '/hr' : ''

  return `${intField(rateLimit, 'MaxRequests')}${label}`
}

function isObject(value: unknown): boolean {
  return typeof value === 'object' && value !== null && !Array.isArray(value)
}

// ---- Security rules ----

function securityKey(item: JsonItem): string {
  const type = field(item, 'TypeID')

  return [textField(item, 'Name'), type === undefined ? '' : String(type), textField(item, 'RoleID'), textField(item, 'Action')].join('|')
}

function securityLabel(item: JsonItem): string {
  return `${textField(item, 'Name')} · ${textField(item, 'RoleID')} · ${textField(item, 'Action')}`
}

function recordText(item: JsonItem): string {
  return field(item, 'Record') === 1 ? 'Owned' : 'All'
}

function propertiesText(item: JsonItem): string {
  const properties = field(item, 'Properties')

  return typeof properties === 'string' && properties.trim() !== '' ? properties : '(none)'
}

function securityRateLimit(item: JsonItem): string {
  const rateLimit = field(item, 'RateLimit')

  return isObject(rateLimit) ? rateLimitText(rateLimit) : 'None'
}

function securityItem(item: JsonItem): SecurityItem {
  return {
    label: securityLabel(item),
    record: recordText(item),
    properties: propertiesText(item),
    rateLimit: securityRateLimit(item),
  }
}

/** Rules by key; of two rules with the same key the first one counts. */
function byKey(items: JsonItem[]): Map<string, JsonItem> {
  const map = new Map<string, JsonItem>()

  for (const item of items) {
    const key = securityKey(item)

    if (!map.has(key)) {
      map.set(key, item)
    }
  }

  return map
}

function securityDiff(oldItems: JsonItem[], newItems: JsonItem[]): ChangeView {
  const oldByKey = byKey(oldItems)
  const newByKey = byKey(newItems)

  const added: SecurityItem[] = []
  const updated: SecurityUpdate[] = []
  const removed: SecurityItem[] = []

  for (const [key, item] of newByKey) {
    if (!oldByKey.has(key)) {
      added.push(securityItem(item))
    }
  }

  for (const [key, oldItem] of oldByKey) {
    const newItem = newByKey.get(key)

    if (!newItem) {
      removed.push(securityItem(oldItem))
    } else if (oldItem.raw !== newItem.raw) {
      updated.push({ label: securityLabel(oldItem), fields: securityFieldDiffs(oldItem, newItem) })
    }
  }

  return { kind: 'security', added, updated, removed }
}

/** The names of a comma-separated list, without duplicates (ignoring case). */
function propertySet(text: string): string[] {
  const names: string[] = []

  for (const name of text.split(',').map((part) => part.trim())) {
    if (name !== '' && !includesIgnoreCase(names, name)) {
      names.push(name)
    }
  }

  return names
}

function includesIgnoreCase(names: string[], name: string): boolean {
  return names.some((other) => other.toLowerCase() === name.toLowerCase())
}

function securityFieldDiffs(oldItem: JsonItem, newItem: JsonItem): SecurityFieldDiff[] {
  const diffs: SecurityFieldDiff[] = []

  const oldRecord = recordText(oldItem)
  const newRecord = recordText(newItem)

  if (oldRecord !== newRecord) {
    diffs.push({ field: 'Record', oldValue: oldRecord, newValue: newRecord })
  }

  // '(none)' takes part as if it were a property name: a rule that gets
  // its first properties shows '(none)' under 'Properties removed'.
  const oldProperties = propertySet(propertiesText(oldItem))
  const newProperties = propertySet(propertiesText(newItem))
  const propertiesRemoved = oldProperties.filter((name) => !includesIgnoreCase(newProperties, name)).sort(compareText)
  const propertiesAdded = newProperties.filter((name) => !includesIgnoreCase(oldProperties, name)).sort(compareText)

  if (propertiesRemoved.length > 0) {
    diffs.push({ field: 'Properties removed', oldValue: propertiesRemoved.join(', '), newValue: '' })
  }

  if (propertiesAdded.length > 0) {
    diffs.push({ field: 'Properties added', oldValue: '', newValue: propertiesAdded.join(', ') })
  }

  const oldRate = securityRateLimit(oldItem)
  const newRate = securityRateLimit(newItem)

  if (oldRate !== newRate) {
    diffs.push({ field: 'Rate limit', oldValue: oldRate, newValue: newRate })
  }

  return diffs
}

function compareText(a: string, b: string): number {
  return a.localeCompare(b)
}

// ---- Other JSON arrays (EntConstraints, EntDefaultOrder) ----

function listDiff(oldItems: JsonItem[], newItems: JsonItem[]): ChangeView {
  const oldRaws = new Set(oldItems.map((item) => item.raw))
  const newRaws = new Set(newItems.map((item) => item.raw))

  return {
    kind: 'list',
    removed: oldItems.filter((item) => !newRaws.has(item.raw)).map(itemLabel),
    added: newItems.filter((item) => !oldRaws.has(item.raw)).map(itemLabel),
  }
}

/** A short name for an array item, built from whichever of the known fields it has. */
function itemLabel(item: JsonItem): string {
  const parts: string[] = []

  for (const name of ['Name', 'Property', 'RoleID', 'Action', 'Direction']) {
    const value = field(item, name)

    if (typeof value === 'string') {
      parts.push(value)
    }
  }

  const properties = field(item, 'Properties')

  if (typeof properties === 'string' && properties !== '') {
    parts.push(properties)
  }

  const record = field(item, 'Record')

  if (typeof record === 'number') {
    parts.push(record === 1 ? 'Record:Owned' : 'Record:All')
  }

  const rateLimit = field(item, 'RateLimit')

  if (isObject(rateLimit)) {
    parts.push(`Rate:${rateLimitText(rateLimit)}`)
  }

  const type = field(item, 'TypeID')

  if (parts.length === 0 && type !== undefined) {
    parts.push(`Type:${typeof type === 'string' ? type : JSON.stringify(type)}`)
  }

  return parts.length > 0 ? parts.join(' ') : '(item)'
}
