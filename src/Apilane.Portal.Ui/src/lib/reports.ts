import { ApiError } from './api'
import type { Schemas } from './api'
import { numberOrNull } from './properties'
import type { Report } from './reportData'

/**
 * The rules of the report editor (ReportEditorSheet): the choices it offers, the form it edits,
 * the request it sends and where the problems the API reports belong. Pure functions, so they can
 * be unit-tested (reports.test.ts). What a panel draws is in lib/reportData.ts.
 */

type ReportField = Schemas['ReportFieldResponse']

/** The report types, in the order the editor offers them, with the names it shows. */
export const reportTypes: readonly { value: string; label: string }[] = [
  { value: 'Grid', label: 'Grid' },
  { value: 'Pie', label: 'Pie chart' },
  { value: 'Line', label: 'Line chart' },
  { value: 'Bar', label: 'Bar chart' },
  { value: 'Radar', label: 'Radar chart' },
  { value: 'StackedBar', label: 'Stacked bar chart' },
]

/** 'Line' as 'Line chart'. A stored type that is none of the six is shown as it is. */
export function reportTypeLabel(type: string): string {
  return reportTypes.find((item) => item.value === type)?.label ?? type
}

/** The time ranges offered with one click. The empty code is no time range. */
export const quickTimeRanges: readonly { code: string; label: string }[] = [
  { code: '', label: 'None (top N)' },
  { code: '24h', label: 'Last 24 hours' },
  { code: '7d', label: 'Last 7 days' },
  { code: '30d', label: 'Last 30 days' },
  { code: '90d', label: 'Last 90 days' },
  { code: '6m', label: 'Last 6 months' },
  { code: '1y', label: 'Last 1 year' },
  { code: '2y', label: 'Last 2 years' },
  { code: '3y', label: 'Last 3 years' },
]

/** The units of a custom time range. */
export const timeUnits: readonly { value: string; label: string }[] = [
  { value: 'h', label: 'hours' },
  { value: 'd', label: 'days' },
  { value: 'm', label: 'months' },
  { value: 'y', label: 'years' },
]

// The form ---------------------------------------------------------------------------------------

/** One series as the editor holds it. GroupBy, Property and Filter are the texts the API stores. */
export interface SeriesForm {
  Label: string
  Entity: string
  GroupBy: string
  Property: string
  Filter: string
}

/** A report as the editor holds it. MaxRecords is a number box: '' while it is empty. */
export interface ReportForm {
  Title: string
  Type: string
  MaxRecords: number | ''
  TimeRange: string
  Series: SeriesForm[]
}

/** An empty series row on `entity` (the editor starts a row at the first entity). */
export function newSeries(entity: string): SeriesForm {
  return { Label: '', Entity: entity, GroupBy: '', Property: '', Filter: '' }
}

/** The form of a new report: a Grid of at most 20 points with one empty series. */
export function newReportForm(entity: string): ReportForm {
  return { Title: '', Type: 'Grid', MaxRecords: 20, TimeRange: '', Series: [newSeries(entity)] }
}

/** The form of a stored report. */
export function reportForm(report: Report): ReportForm {
  return {
    Title: report.Title,
    Type: report.Type,
    MaxRecords: report.MaxRecords,
    TimeRange: report.TimeRange ?? '',
    Series: report.Series.map((series) => ({
      Label: series.Label,
      Entity: series.Entity,
      GroupBy: series.GroupBy,
      Property: series.Property,
      Filter: series.Filter ?? '',
    })),
  }
}

/** A row with no label, group-by and property: the editor leaves it out of the save. */
function isEmptyRow(series: SeriesForm): boolean {
  return series.Label.trim() === '' && series.GroupBy === '' && series.Property === ''
}

/**
 * The body of the save, and `rows`: for each series sent, its place in the form. Empty rows are not
 * sent, so 'Series[1]' of an answer may be another row of the form: reportErrors maps it back.
 */
export function reportRequest(form: ReportForm): { body: Schemas['ReportRequest']; rows: number[] } {
  const rows = form.Series.map((_, index) => index).filter((index) => {
    const series = form.Series[index]

    return series !== undefined && !isEmptyRow(series)
  })

  return {
    rows,
    body: {
      Title: form.Title,
      Type: form.Type,
      // The contract types it as a number; an empty box goes as null and the API answers 'Required'.
      MaxRecords: numberOrNull(form.MaxRecords) as number,
      TimeRange: form.TimeRange === '' ? null : form.TimeRange,
      Series: rows.flatMap((index) => {
        const series = form.Series[index]

        return series ? [{ ...series, Filter: series.Filter === '' ? null : series.Filter }] : []
      }),
    },
  }
}

// Problems of a failed save ----------------------------------------------------------------------

/** What the editor shows for a failed save. */
export interface ReportErrors {
  /** Title, Type, MaxRecords, TimeRange and Series (the list as a whole) to their message. */
  fields: Record<string, string>
  /** Place of a series in the form to the message of each of its fields; '' is the row as a whole. */
  series: Record<number, Record<string, string>>
  /** Shown above the form: everything that has no place of its own. */
  message: string | undefined
}

const reportFieldNames = ['Title', 'Type', 'MaxRecords', 'TimeRange', 'Series']
const seriesFieldNames = ['', 'Label', 'Entity', 'GroupBy', 'Property', 'Filter']

/**
 * Turns the error of a failed save into what the editor shows. The API names the place of each
 * problem ('Title', 'Series[1].GroupBy'); `rows` is what reportRequest returned with the body.
 */
export function reportErrors(error: Error | undefined, rows: readonly number[]): ReportErrors {
  const result: ReportErrors = { fields: {}, series: {}, message: undefined }

  if (!error) {
    return result
  }

  let details = error instanceof ApiError ? error.errors : []

  if (details.length === 0 && error instanceof ApiError && error.property) {
    details = [{ Property: error.property, Message: error.message }]
  }

  if (details.length === 0) {
    return { ...result, message: error.message }
  }

  const other: string[] = []

  for (const detail of details) {
    const match = /^Series\[(\d+)\](?:\.(\w+))?$/.exec(detail.Property)
    const row = match ? rows[Number(match[1])] : undefined
    const field = match?.[2] ?? ''

    if (row !== undefined && seriesFieldNames.includes(field)) {
      const messages = (result.series[row] ??= {})
      messages[field] ??= detail.Message
    } else if (reportFieldNames.includes(detail.Property)) {
      result.fields[detail.Property] ??= detail.Message
    } else {
      other.push(detail.Message)
    }
  }

  return { ...result, message: other.length > 0 ? other.join(' ') : undefined }
}

// Values and group-bys of a series ---------------------------------------------------------------

/** What report-fields offers as the texts a series stores: 'Name' for a field without subs, 'Name.Sub' for each sub. */
export function fieldNames(fields: readonly ReportField[]): string[] {
  return fields.flatMap((field) => (field.Subs.length === 0 ? [field.Name] : field.Subs.map((sub) => `${field.Name}.${sub}`)))
}

/** 'Created.Year,Created.Month' as its parts. */
export function splitGroupBy(text: string): string[] {
  return text
    .split(',')
    .map((part) => part.trim())
    .filter((part) => part !== '')
}

/**
 * Ticks or unticks one group-by. The result lists the offered ones in the order report-fields has
 * them, not in the order they were ticked. Parts that are not offered
 * (any more) stay, in front, until they are unticked themselves.
 */
export function toggleGroupBy(offered: readonly string[], text: string, name: string): string {
  const current = splitGroupBy(text)
  const next = current.includes(name) ? current.filter((part) => part !== name) : [...current, name]

  return [...next.filter((part) => !offered.includes(part)), ...offered.filter((part) => next.includes(part))].join(',')
}

/** The parts of a stored text that report-fields does not offer for this entity and report type. */
export function notOffered(offered: readonly string[], text: string): string[] {
  return splitGroupBy(text).filter((part) => !offered.includes(part))
}
