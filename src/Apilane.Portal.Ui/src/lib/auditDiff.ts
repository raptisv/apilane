/**
 * Turns one changed property of an audit log entry into what the detail table shows. Every
 * display rule of the audit log is here; components/AuditLogDetail.vue uses it for the instance
 * and the application audit log.
 *
 * Pure functions only, no Vue: the rules can be unit-tested with stored Changes payloads.
 * Old and new values arrive as the raw stored strings. Items of a JSON array are compared by
 * their raw text, so the result depends on the stored text itself.
 */

import { accountDisplayName } from './agents'

/** Account targets have display names; arbitrary entity, application and property names stay exact. */
export function auditTargetName(entityType: string, identifier: string): string {
  return ['Agent', 'User', 'User Role', 'Collaboration', 'Agent Permissions'].includes(entityType)
    ? accountDisplayName(identifier)
    : identifier
}

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
  /** A text that changed (the SQL of a custom endpoint): what was removed and added, line by line like a pull request. */
  | { kind: 'lines'; lines: DiffLine[] }
  /** Everything else: the stored text, one cell per value column. `old` cells are shown dimmed. */
  | { kind: 'text'; cells: { text: string; old: boolean }[] }

/** A piece of a changed line; `changed` ones are the words that differ from the paired line. */
export interface DiffSegment {
  text: string
  changed: boolean
}

export interface DiffPart {
  type: 'same' | 'added' | 'removed'
  text: string
  /** Line numbers in the old and the new text; a removed line has no new one, an added line no old one. */
  oldNo?: number
  newNo?: number
  /** Set on a line that was changed into another: its words, the differing ones marked. */
  segments?: DiffSegment[]
}

/** One line of a line diff. A 'gap' stands for `hidden` unchanged lines left out. */
export type DiffLine = DiffPart | { type: 'gap'; text: string; hidden: number }

/** Unchanged lines kept around each change. */
const contextLines = 3

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

export function describeChange(action: string, change: AuditChange, entityType?: string): ChangeView {
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

  // Only known account fields are names. Other strings, including mail settings and JSON,
  // remain the stored value even when they happen to end with the agent suffix.
  const isAccountField = (entityType === 'Collaboration' && change.Property === 'UserEmail')
    || (entityType === 'Application' && change.Property === 'AdminEmail')
    || (entityType === 'Agent' && ['Email', 'UserName', 'NormalizedEmail', 'NormalizedUserName'].includes(change.Property))
  const displayValue = (value: string | null | undefined) => value == null
    ? '<null>'
    : isAccountField ? accountDisplayName(value) : value
  if (action === 'Modified' && !isAccountField && change.OldValue != null && change.NewValue != null) {
    const textView = textDiff(change.OldValue, change.NewValue)

    if (textView) {
      return textView
    }
  }

  const oldCell = { text: displayValue(change.OldValue), old: true }
  const newCell = { text: displayValue(change.NewValue), old: false }

  if (action === 'Modified') {
    return { kind: 'text', cells: [oldCell, newCell] }
  }

  return { kind: 'text', cells: [action === 'Created' ? newCell : oldCell] }
}

// ---- Changed texts ----

/**
 * The diff of a changed text, line by line like a pull request. A value of one word (a name, a number, an
 * e-mail address) is not text to compare: old and new stay side by side.
 */
function textDiff(oldText: string, newText: string): ChangeView | undefined {
  if (oldText === newText || !/\s/.test(oldText + newText)) {
    return undefined
  }

  const split = (text: string) => text.split(/\r\n|\r|\n/)

  return { kind: 'lines', lines: foldContext(numberLines(pairInline(diffSequences(split(oldText), split(newText))))) }
}

/** The longest common subsequence of two lists: what stayed, what left the old one, what came into the new one. */
function diffSequences(oldItems: string[], newItems: string[]): DiffPart[] {
  // Identical heads and tails are matched first, so the table below holds only what changed.
  let head = 0

  while (head < oldItems.length && head < newItems.length && oldItems[head] === newItems[head]) {
    head++
  }

  let tail = 0

  while (
    tail < oldItems.length - head &&
    tail < newItems.length - head &&
    oldItems[oldItems.length - 1 - tail] === newItems[newItems.length - 1 - tail]
  ) {
    tail++
  }

  const a = oldItems.slice(head, oldItems.length - tail)
  const b = newItems.slice(head, newItems.length - tail)
  const parts: DiffPart[] = oldItems.slice(0, head).map((text) => ({ type: 'same', text }))

  if (a.length * b.length > 4_000_000) {
    // Too large for the table: the whole middle is shown as replaced.
    parts.push(...a.map((text): DiffPart => ({ type: 'removed', text })), ...b.map((text): DiffPart => ({ type: 'added', text })))
  } else {
    const width = b.length + 1
    const table = new Uint32Array((a.length + 1) * width)

    for (let i = a.length - 1; i >= 0; i--) {
      for (let j = b.length - 1; j >= 0; j--) {
        table[i * width + j] = a[i] === b[j] ? table[(i + 1) * width + j + 1] + 1 : Math.max(table[(i + 1) * width + j], table[i * width + j + 1])
      }
    }

    let i = 0
    let j = 0

    while (i < a.length && j < b.length) {
      if (a[i] === b[j]) {
        parts.push({ type: 'same', text: a[i] })
        i++
        j++
      } else if (table[(i + 1) * width + j] >= table[i * width + j + 1]) {
        parts.push({ type: 'removed', text: a[i++] })
      } else {
        parts.push({ type: 'added', text: b[j++] })
      }
    }

    parts.push(...a.slice(i).map((text): DiffPart => ({ type: 'removed', text })), ...b.slice(j).map((text): DiffPart => ({ type: 'added', text })))
  }

  parts.push(...oldItems.slice(oldItems.length - tail).map((text): DiffPart => ({ type: 'same', text })))

  return parts
}

/** Words with their spaces and punctuation, so the pieces join back into the line. */
function tokens(text: string): string[] {
  return text.match(/\s+|\w+|[^\s\w]/g) ?? []
}

/**
 * A run of removed lines followed by a run of added lines is a change of those lines: the first removed
 * line pairs with the first added one, and so on. A pair gets the words that differ marked, like GitHub.
 */
function pairInline(parts: DiffPart[]): DiffPart[] {
  const result: DiffPart[] = []
  let i = 0

  while (i < parts.length) {
    if (parts[i].type !== 'removed') {
      result.push(parts[i++])
      continue
    }

    const removed: DiffPart[] = []
    const added: DiffPart[] = []

    while (i < parts.length && parts[i].type === 'removed') {
      removed.push({ ...parts[i++] })
    }

    while (i < parts.length && parts[i].type === 'added') {
      added.push({ ...parts[i++] })
    }

    for (let k = 0; k < Math.min(removed.length, added.length); k++) {
      const words = diffSequences(tokens(removed[k].text), tokens(added[k].text))

      removed[k].segments = mergeSegments(words, 'removed')
      added[k].segments = mergeSegments(words, 'added')
    }

    result.push(...removed, ...added)
  }

  return result
}

/** The pieces of one side of a changed line: the common words and the words only this side has. */
function mergeSegments(words: DiffPart[], side: 'removed' | 'added'): DiffSegment[] {
  const segments: DiffSegment[] = []

  for (const word of words) {
    if (word.type !== 'same' && word.type !== side) {
      continue
    }

    const changed = word.type !== 'same'
    const last = segments[segments.length - 1]

    if (last && last.changed === changed) {
      last.text += word.text
    } else {
      segments.push({ text: word.text, changed })
    }
  }

  return segments
}

/** The line numbers of the old and of the new text, like the two gutters of a pull request diff. */
function numberLines(parts: DiffPart[]): DiffPart[] {
  let oldNo = 0
  let newNo = 0

  return parts.map((part) => {
    if (part.type !== 'added') {
      oldNo++
    }

    if (part.type !== 'removed') {
      newNo++
    }

    return { ...part, oldNo: part.type === 'added' ? undefined : oldNo, newNo: part.type === 'removed' ? undefined : newNo }
  })
}

/** Keeps the lines around each change and folds the unchanged lines between them into a gap. */
function foldContext(lines: DiffPart[]): DiffLine[] {
  const keep = lines.map((line) => line.type !== 'same')

  for (let i = 0; i < lines.length; i++) {
    if (lines[i].type !== 'same') {
      for (let k = Math.max(0, i - contextLines); k <= Math.min(lines.length - 1, i + contextLines); k++) {
        keep[k] = true
      }
    }
  }

  const folded: DiffLine[] = []

  for (let i = 0; i < lines.length; i++) {
    if (keep[i]) {
      folded.push(lines[i])
      continue
    }

    const last = folded[folded.length - 1]

    if (last && last.type === 'gap') {
      last.hidden++
      last.text = `${last.hidden} unchanged lines`
    } else {
      folded.push({ type: 'gap', text: '1 unchanged line', hidden: 1 })
    }
  }

  return folded
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
