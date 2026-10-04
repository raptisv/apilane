/**
 * The rules of the filter builder (FilterBuilder.vue): a flat list of conditions that must all hold,
 * read from and written to the JSON text the data API takes as its Filter. Pure functions, so
 * they can be unit-tested (filterBuilder.test.ts).
 *
 * The data browser's column filters (filterOperators and filterParam of lib/records.ts) write the
 * same JSON shape but offer only 'contains' and 'equal'; the builder offers every operator.
 */

/** Every operator of the data API, by its first name, in the order the builder offers them (FilterData.FilterOperators). */
export const allFilterOperators = [
  'equal',
  'notequal',
  'greater',
  'greaterorequal',
  'less',
  'lessorequal',
  'startswith',
  'endswith',
  'contains',
  'notcontains',
] as const

// The other names the data API takes for an operator.
const operatorAliases: Record<string, string> = {
  eq: 'equal',
  '==': 'equal',
  '=': 'equal',
  neq: 'notequal',
  '!=': 'notequal',
  '<>': 'notequal',
  g: 'greater',
  '>': 'greater',
  ge: 'greaterorequal',
  '>=': 'greaterorequal',
  l: 'less',
  '<': 'less',
  le: 'lessorequal',
  '<=': 'lessorequal',
  sw: 'startswith',
  ew: 'endswith',
  like: 'contains',
  nc: 'notcontains',
}

/** One condition of the builder. `value` is text; the text 'null' stands for the null value. */
export interface FilterRow {
  property: string
  operator: string
  value: string
}

/** A new condition: the first property, 'equal', an empty value. */
export function newFilterRow(properties: readonly string[]): FilterRow {
  return { property: properties[0] ?? '', operator: allFilterOperators[0], value: '' }
}

/**
 * A stored filter as the builder shows it: its conditions, or `raw` (the text itself) when it is
 * not something the builder can show without changing its meaning - not JSON, conditions joined
 * with OR, nested groups, an operator or a value it does not know.
 */
export type ParsedFilter = { rows: FilterRow[]; raw?: undefined } | { rows?: undefined; raw: string }

/** Reads a stored filter. No filter (null, empty) is an empty list of conditions. */
export function parseFilter(text: string | null | undefined): ParsedFilter {
  if (!text || text.trim() === '') {
    return { rows: [] }
  }

  const rows = filterRows(text)

  return rows ? { rows } : { raw: text }
}

function filterRows(text: string): FilterRow[] | undefined {
  let parsed: unknown

  try {
    parsed = JSON.parse(text)
  } catch {
    return undefined
  }

  const filter = fields(parsed)
  const filters = filter?.filters

  if (!filter || !Array.isArray(filters) || filter.property !== undefined) {
    return undefined
  }

  // With one condition or none, AND and OR mean the same.
  if (filters.length > 1 && String(filter.logic ?? 'and').toLowerCase() !== 'and') {
    return undefined
  }

  const rows: FilterRow[] = []

  for (const item of filters) {
    const condition = fields(item)
    const operator = operatorName(condition?.operator)

    if (!condition || typeof condition.property !== 'string' || condition.filters != null || !operator) {
      return undefined
    }

    const value = condition.value

    if (typeof value === 'object' && value !== null) {
      return undefined
    }

    rows.push({ property: condition.property, operator, value: value === null || value === undefined ? 'null' : String(value) })
  }

  return rows
}

// The data API reads the names of a filter whatever their letter case.
function fields(value: unknown): Record<string, unknown> | undefined {
  if (typeof value !== 'object' || value === null || Array.isArray(value)) {
    return undefined
  }

  return Object.fromEntries(Object.entries(value).map(([name, item]) => [name.toLowerCase(), item]))
}

function operatorName(value: unknown): string | undefined {
  const text = String(value ?? '').trim().toLowerCase()
  const name = operatorAliases[text] ?? text

  return (allFilterOperators as readonly string[]).includes(name) ? name : undefined
}

/**
 * The JSON text of the conditions: { Logic: 'and', Filters: [{ Property, Operator, Value }] }, or
 * '' for no condition (no filter). A value is sent as text; the text 'null' is sent as null.
 */
export function filterText(rows: readonly FilterRow[]): string {
  if (rows.length === 0) {
    return ''
  }

  return JSON.stringify({
    Logic: 'and',
    Filters: rows.map((row) => ({ Property: row.property, Operator: row.operator, Value: row.value === 'null' ? null : row.value })),
  })
}

/** A stored filter in a few words, for the button that opens the builder: 'No filter', '2 conditions', 'Custom filter'. */
export function filterSummary(text: string | null | undefined): string {
  const parsed = parseFilter(text)

  if (!parsed.rows) {
    return 'Custom filter'
  }

  return parsed.rows.length === 0 ? 'No filter' : parsed.rows.length === 1 ? '1 condition' : `${parsed.rows.length} conditions`
}
