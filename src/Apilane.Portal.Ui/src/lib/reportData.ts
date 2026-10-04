import type { Schemas } from './api'
import { serverBase } from './apiServer'

/**
 * Everything a report panel works out from its definition and from the rows the API server
 * answers: the time window, the Stats/Aggregate call of each series, the badges and legend labels,
 * the shared x-axis, the table of a Grid and the Chart.js configuration of a chart. A copy of what
 * the classic page does (Views/Shared/Report.cshtml and renderReportPanel with its helpers in
 * wwwroot/assets/custom.js), as pure functions, so they can be unit-tested (reportData.test.ts).
 *
 * The rows come from GET {ServerUrl}/api/Stats/Aggregate, called by the browser through apiServer
 * (lib/apiServer.ts). That answer is not in the Portal contract: it is a list of AggregateRow.
 */
export type Report = Schemas['ReportResponse']
export type ReportSeries = Schemas['ReportSeriesResponse']
type Group = Schemas['ReportSeriesGroupResponse']

/** One row of Stats/Aggregate: a value per group column ('Created_year') and the aggregate ('ID_count'). */
export type AggregateRow = Record<string, unknown>

// The time range ---------------------------------------------------------------------------------

export type TimeUnit = 'h' | 'd' | 'm' | 'y'

export interface TimeRange {
  amount: number
  unit: TimeUnit
}

const unitWords: Record<TimeUnit, string> = { h: 'hour', d: 'day', m: 'month', y: 'year' }

/** '7d' as { amount: 7, unit: 'd' }. Anything else (empty, '0d', '7w') is no time range (DBWS_ReportPanel.TryParseTimeRange). */
export function parseTimeRange(code: string | null | undefined): TimeRange | undefined {
  const match = /^(\d+)([hdmy])$/.exec((code ?? '').trim().toLowerCase())
  const amount = match ? Number(match[1]) : 0

  return match && amount > 0 ? { amount, unit: match[2] as TimeUnit } : undefined
}

/** '7d' as 'Last 7 days'; undefined for no time range (DBWS_ReportPanel.GetTimeRangeDisplay). */
export function timeRangeLabel(code: string | null | undefined): string | undefined {
  const range = parseTimeRange(code)

  return range ? `Last ${range.amount} ${unitWords[range.unit]}${range.amount === 1 ? '' : 's'}` : undefined
}

/** `date` moved by whole months in UTC. A day the target month does not have becomes its last day, as .NET's AddMonths does. */
function addMonths(date: Date, months: number): Date {
  const total = date.getUTCFullYear() * 12 + date.getUTCMonth() + months
  const year = Math.floor(total / 12)
  const month = total - year * 12
  const result = new Date(date)

  // Day 0 of the next month is the last day of the target month.
  result.setUTCFullYear(year, month + 1, 0)

  const lastDay = result.getUTCDate()

  result.setUTCFullYear(year, month, Math.min(date.getUTCDate(), lastDay))
  return result
}

/**
 * The time window of a time range, in Unix milliseconds: from `now` minus the range until `now`
 * (DBWS_ReportPanel.GetTimeWindowMs). The panel computes it at every load and every refresh.
 */
export function timeWindow(code: string | null | undefined, now: Date): { start: number; end: number } | undefined {
  const range = parseTimeRange(code)

  if (!range) {
    return undefined
  }

  const end = now.getTime()

  switch (range.unit) {
    case 'h':
      return { start: end - range.amount * 3_600_000, end }
    case 'd':
      return { start: end - range.amount * 86_400_000, end }
    case 'm':
      return { start: addMonths(now, -range.amount).getTime(), end }
    case 'y':
      return { start: addMonths(now, -range.amount * 12).getTime(), end }
  }
}

// The call of one series -------------------------------------------------------------------------

/** The query of GET {ServerUrl}/api/Stats/Aggregate. */
export type AggregateQuery = {
  Entity: string
  Properties: string
  Filter: string | undefined
  GroupBy: string
  PageIndex: number
  PageSize: number
}

/**
 * The filter a series sends. A windowed series (the report has a time range and the series is
 * grouped by a date first) sends its stored filter AND the time window on that date
 * (DBWS_ReportSeries.BuildWindowedFilter); every other series sends its stored filter as it is.
 * Throws when the stored filter of a windowed series is not JSON.
 */
export function seriesFilter(report: Report, series: ReportSeries, now: Date): string | undefined {
  const stored = series.Filter && series.Filter.trim() !== '' ? series.Filter : undefined
  const window = series.Windowed && series.WindowProperty ? timeWindow(report.TimeRange, now) : undefined

  if (!window) {
    return stored
  }

  const filters: unknown[] = []

  if (stored) {
    try {
      filters.push(JSON.parse(stored))
    } catch {
      throw new Error('The filter of this series cannot be read.')
    }
  }

  filters.push(
    { Property: series.WindowProperty, Operator: 'greaterorequal', Value: window.start },
    { Property: series.WindowProperty, Operator: 'lessorequal', Value: window.end },
  )

  return JSON.stringify({ Logic: 'AND', Filters: filters })
}

/**
 * The Stats/Aggregate call of one series (DBWS_ReportSeries.GetApiUrl): its own entity, value and
 * group-by, and the report's Top N as the page size. The classic page also sends 'Sort=Desc',
 * which the API server does not read: groups come newest or largest first either way.
 */
export function seriesQuery(report: Report, series: ReportSeries, now: Date): AggregateQuery {
  const compact = (text: string) => text.replace(/\s/g, '')

  return {
    Entity: compact(series.Entity),
    Properties: compact(series.Property),
    Filter: seriesFilter(report, series, now),
    GroupBy: compact(series.GroupBy),
    PageIndex: 1,
    PageSize: report.MaxRecords,
  }
}

/** The address of a series' call, for the 'View API endpoint' dialog: the query URL-encoded, with the application token. */
export function aggregateUrl(serverUrl: string, appToken: string, query: AggregateQuery): string {
  const pairs = Object.entries({ ...query, AppToken: appToken })
    .filter(([, value]) => value !== undefined)
    .map(([name, value]) => `${name}=${encodeURIComponent(String(value))}`)

  return `${serverBase(serverUrl)}/api/Stats/Aggregate?${pairs.join('&')}`
}

// Badges and legend labels -----------------------------------------------------------------------

export interface PanelScope {
  /** The time range badge ('Last 7 days'): every series that can run is windowed. */
  windowLabel: string | undefined
  /** The 'Top 20' badge: no series that can run is windowed. */
  topNLabel: string | undefined
  /** The legend label of each series, in the order of report.Series. */
  labels: string[]
}

/**
 * What a panel covers (Report.cshtml:25-43): the time range applies to windowed series, Top N to
 * the others. A panel with both kinds shows no badge; each series says its own scope in its label.
 * A series with an Error does not count.
 */
export function panelScope(report: Report): PanelScope {
  const runnable = report.Series.filter((series) => !series.Error)
  const rangeLabel = report.TimeRangeLabel ?? timeRangeLabel(report.TimeRange)
  const topNLabel = `Top ${report.MaxRecords}`

  const anyWindowed = runnable.some((series) => series.Windowed)
  const anyTopN = runnable.some((series) => !series.Windowed)
  const mixed = anyWindowed && anyTopN

  return {
    windowLabel: !mixed && anyWindowed ? rangeLabel : undefined,
    topNLabel: !mixed && anyTopN ? topNLabel : undefined,
    labels: report.Series.map((series) =>
      mixed ? `${series.Label} · ${series.Windowed ? (rangeLabel ?? '') : topNLabel}` : series.Label,
    ),
  }
}

// The shared x-axis ------------------------------------------------------------------------------

/** A column of a row, whatever its letter case: the casing differs between database providers (ID_Count, ID_count). */
export function rowValue(row: AggregateRow, column: string): unknown {
  if (row[column] !== undefined) {
    return row[column]
  }

  const lower = column.toLowerCase()
  const name = Object.keys(row).find((key) => key.toLowerCase() === lower)

  return name === undefined ? undefined : row[name]
}

interface LabelPart {
  text: string
  /** The moment a date part stands for, to sort by; undefined for any other part. */
  time: number | undefined
}

const dateParts = ['year', 'month', 'day', 'hour', 'minute', 'second'] as const

/** The parts of one date property as 'd/m/y h:m:s', only the parts grouped by and without leading zeros (custom.js getLabelForGroup). */
function datePart(groups: readonly Group[], row: AggregateRow): LabelPart {
  const [year, month, day, hour, minute, second] = dateParts.map((part) => {
    const group = groups.find((item) => item.Part?.toLowerCase() === part)

    return group ? String(rowValue(row, group.Column) ?? '') : undefined
  })

  const text =
    (day === undefined ? '' : `${day}/`) +
    (month === undefined ? '' : `${month}/`) +
    (year === undefined ? '' : `${year} `) +
    (hour === undefined ? '' : hour + (minute === undefined ? '' : ':')) +
    (minute === undefined ? '' : minute + (second === undefined ? '' : ':')) +
    (second ?? '')

  const time = Date.UTC(
    Number(year ?? 0),
    Number(month ?? 1) - 1,
    Number(day ?? 1),
    Number(hour ?? 0),
    Number(minute ?? 0),
    Number(second ?? 0),
  )

  return { text: text.trim(), time: Number.isFinite(time) ? time : undefined }
}

/**
 * The label of one row, in parts (custom.js getLabelForGroup): the group-bys of one type together,
 * types in the order they first appear, and one part per property. A date grouped by its parts
 * gives one part; any other property gives its value.
 */
function labelParts(groups: readonly Group[], row: AggregateRow): LabelPart[] {
  const parts: LabelPart[] = []

  for (const type of new Set(groups.map((group) => group.Type))) {
    const ofType = groups.filter((group) => group.Type === type)

    for (const property of new Set(ofType.map((group) => group.Property))) {
      const ofProperty = ofType.filter((group) => group.Property === property)
      const byPart = ofProperty.filter((group) => group.Part)
      const first = ofProperty[0]

      if (type === 'Date' && byPart.length > 0) {
        parts.push(datePart(byPart, row))
      } else if (first) {
        parts.push({ text: String(rowValue(row, first.Column) ?? ''), time: undefined })
      }
    }
  }

  return parts
}

/** The rows one series loaded, with the label it has in the legend. */
export interface SeriesRows {
  label: string
  series: ReportSeries
  rows: AggregateRow[]
}

/** The series of a panel on one shared x-axis. */
export interface CombinedReport {
  /** The x-axis: every group any series has, in order. */
  labels: string[]
  /** Per series its value at each label; undefined where the series has no such group. */
  series: { label: string; values: unknown[] }[]
}

/**
 * Puts the series of a panel on one x-axis (custom.js renderCombinedReport): the label of a row is
 * its parts joined with ' / ', the axis holds every label of every series once, and it is sorted by
 * time when the first part is a date and by text otherwise.
 */
export function combineSeries(results: readonly SeriesRows[]): CombinedReport {
  const order: { key: string; sort: number | string }[] = []
  const seen = new Set<string>()

  const maps = results.map((result) => {
    const values = new Map<string, unknown>()
    const column = result.series.Aggregate?.Column

    if (!column) {
      return values
    }

    for (const row of result.rows) {
      const parts = labelParts(result.series.Groups, row)
      const key = parts.map((part) => part.text).join(' / ')

      values.set(key, rowValue(row, column))

      if (!seen.has(key)) {
        seen.add(key)
        order.push({ key, sort: parts[0]?.time ?? key })
      }
    }

    return values
  })

  order.sort((a, b) =>
    typeof a.sort === 'number' && typeof b.sort === 'number' ? a.sort - b.sort : String(a.sort).localeCompare(String(b.sort)),
  )

  const labels = order.map((item) => item.key)

  return {
    labels,
    series: results.map((result, index) => ({
      label: result.label,
      values: labels.map((label) => maps[index]?.get(label)),
    })),
  }
}

/** Whether no series has a row: the panel then says 'No data'. */
export function hasNoRows(results: readonly SeriesRows[]): boolean {
  return results.every((result) => result.rows.length === 0)
}

// Grid -------------------------------------------------------------------------------------------

/** A value in the table of a Grid report: its text, or nothing for a missing or null one. */
export function gridText(value: unknown): string {
  return value === undefined || value === null ? '' : String(value)
}

// Charts -----------------------------------------------------------------------------------------

/** A value as a number for a chart, or null when it is missing or not numeric (custom.js toNumberOrNull). */
export function toNumberOrNull(value: unknown): number | null {
  if (value === undefined || value === null || value === '') {
    return null
  }

  const number = Number(value)

  return Number.isNaN(number) ? null : number
}

/** A '#rrggbb' colour as rgba() with the given alpha (custom.js hexWithAlpha). Any other colour is returned as it is. */
export function withAlpha(color: string, alpha: number): string {
  if (!/^#[0-9a-f]{6}$/i.test(color)) {
    return color
  }

  const number = parseInt(color.slice(1), 16)

  return `rgba(${(number >> 16) & 255},${(number >> 8) & 255},${number & 255},${alpha})`
}

/** What createReportChart (lib/reportChart.ts) hands to Chart.js. */
export interface ReportChartConfig {
  type: 'pie' | 'bar' | 'line' | 'radar'
  data: { labels: string[]; datasets: Record<string, unknown>[] }
  options: Record<string, unknown>
}

/**
 * The Chart.js configuration of a report that is not a Grid (custom.js renderCombinedReport):
 * - Pie: the first series only, one slice per label, legend on the right.
 * - Bar, StackedBar, Radar, and Line for anything else: one dataset per series, legend below, the
 *   value axis starting at 0. A stacked bar sums the series at each label.
 * `palette` are the series colours (the --chart-N tokens of style.css), used in turn.
 */
export function chartConfig(type: string, report: CombinedReport, palette: readonly string[]): ReportChartConfig {
  const colorAt = (index: number) => (palette.length > 0 ? palette[index % palette.length] : undefined)

  if (type === 'Pie') {
    return {
      type: 'pie',
      data: {
        labels: report.labels,
        datasets: [
          {
            data: (report.series[0]?.values ?? []).map(toNumberOrNull),
            backgroundColor: report.labels.map((_, index) => colorAt(index)),
          },
        ],
      },
      options: { responsive: true, maintainAspectRatio: false, plugins: { legend: { display: true, position: 'right' } } },
    }
  }

  const chartType = type === 'Bar' || type === 'StackedBar' ? 'bar' : type === 'Radar' ? 'radar' : 'line'
  const radar = chartType === 'radar'

  return {
    type: chartType,
    data: {
      labels: report.labels,
      datasets: report.series.map((series, index) => {
        const color = colorAt(index)

        return {
          label: series.label,
          data: series.values.map(toNumberOrNull),
          borderColor: color,
          borderWidth: 2,
          // A radar fills a see-through shape; lines and bars use the plain series colour.
          backgroundColor: radar && color ? withAlpha(color, 0.3) : color,
          pointBackgroundColor: color,
          pointRadius: radar ? 2 : 3,
          fill: radar,
          spanGaps: true,
        }
      }),
    },
    options: {
      responsive: true,
      maintainAspectRatio: false,
      // Lines and bars: the tooltip shows every series at the x position under the pointer.
      // Radar: the nearest point, because a round axis has no x position.
      interaction: radar ? { mode: 'nearest', intersect: true } : { mode: 'index', intersect: false },
      plugins: { legend: { display: true, position: 'bottom' } },
      scales: radar
        ? { r: { suggestedMin: 0, ticks: { display: false } } }
        : type === 'StackedBar'
          ? { x: { stacked: true }, y: { stacked: true, suggestedMin: 0 } }
          : { y: { suggestedMin: 0 } },
    },
  }
}
