import { describe, expect, it } from 'vitest'
import {
  aggregateUrl,
  chartConfig,
  combineSeries,
  gridText,
  hasNoRows,
  panelScope,
  parseTimeRange,
  rowValue,
  seriesFilter,
  seriesQuery,
  timeRangeLabel,
  timeWindow,
  toNumberOrNull,
  withAlpha,
} from './reportData'
import type { Report, ReportSeries } from './reportData'

const palette = ['#4e79a7', '#f28e2b', '#e15759']

function group(Property: string, Type: string, Part?: string): ReportSeries['Groups'][number] {
  return { Property, Type, Part, Column: Part ? `${Property}_${Part}` : Property }
}

/** A series as ReportResponse carries it: 'ID.Count' grouped by the given groups. */
function series(Label: string, groups: ReportSeries['Groups'], extra: Partial<ReportSeries> = {}): ReportSeries {
  return {
    Label,
    Entity: 'Orders',
    GroupBy: groups.map((item) => (item.Part ? `${item.Property}.${item.Part}` : item.Property)).join(','),
    Property: 'ID.Count',
    Filter: null,
    Aggregate: { Property: 'ID', Function: 'Count', Column: 'ID_count' },
    Groups: groups,
    Windowed: false,
    WindowProperty: null,
    Error: null,
    ...extra,
  }
}

function report(Series: ReportSeries[], extra: Partial<Report> = {}): Report {
  return {
    ID: 1,
    Title: 'Orders',
    Type: 'Line',
    X: 0,
    Y: 0,
    Width: 6,
    Height: 4,
    MaxRecords: 20,
    TimeRange: null,
    TimeRangeLabel: null,
    DateModified: '2026-01-01T00:00:00Z',
    Series,
    ...extra,
  }
}

const byMonth = [group('Created', 'Date', 'year'), group('Created', 'Date', 'month')]
const now = new Date('2026-03-31T10:20:30.000Z')

describe('parseTimeRange', () => {
  it('reads a number and a unit', () => {
    expect(parseTimeRange('24h')).toEqual({ amount: 24, unit: 'h' })
    expect(parseTimeRange(' 6M ')).toEqual({ amount: 6, unit: 'm' })
  })

  it('takes anything else as no time range', () => {
    for (const code of [null, undefined, '', 'd', '0d', '7w', '-7d', '7.5d', 'last week']) {
      expect(parseTimeRange(code)).toBeUndefined()
    }
  })
})

describe('timeRangeLabel', () => {
  it('says the range in words', () => {
    expect(timeRangeLabel('7d')).toBe('Last 7 days')
    expect(timeRangeLabel('1y')).toBe('Last 1 year')
    expect(timeRangeLabel('24h')).toBe('Last 24 hours')
    expect(timeRangeLabel('6m')).toBe('Last 6 months')
    expect(timeRangeLabel('')).toBeUndefined()
  })
})

describe('timeWindow', () => {
  it('ends now and starts the range earlier', () => {
    expect(timeWindow('24h', now)).toEqual({ start: Date.parse('2026-03-30T10:20:30.000Z'), end: now.getTime() })
    expect(timeWindow('7d', now)).toEqual({ start: Date.parse('2026-03-24T10:20:30.000Z'), end: now.getTime() })
  })

  it('goes back whole months, to the last day of a shorter month', () => {
    expect(timeWindow('1m', now)?.start).toBe(Date.parse('2026-02-28T10:20:30.000Z'))
    expect(timeWindow('3m', now)?.start).toBe(Date.parse('2025-12-31T10:20:30.000Z'))
    expect(timeWindow('6m', new Date('2026-03-15T00:00:00.000Z'))?.start).toBe(Date.parse('2025-09-15T00:00:00.000Z'))
  })

  it('goes back whole years, 29 February becoming 28', () => {
    expect(timeWindow('2y', now)?.start).toBe(Date.parse('2024-03-31T10:20:30.000Z'))
    expect(timeWindow('1y', new Date('2024-02-29T12:00:00.000Z'))?.start).toBe(Date.parse('2023-02-28T12:00:00.000Z'))
  })

  it('is undefined without a time range', () => {
    expect(timeWindow(null, now)).toBeUndefined()
    expect(timeWindow('soon', now)).toBeUndefined()
  })
})

describe('seriesFilter', () => {
  const stored = '{"Logic":"and","Filters":[{"Property":"Status","Operator":"equal","Value":"New York"}]}'

  it('sends the stored filter of a series that is not windowed as it is', () => {
    expect(seriesFilter(report([], { TimeRange: '7d' }), series('A', [group('Status', 'String')], { Filter: stored }), now)).toBe(stored)
    expect(seriesFilter(report([]), series('A', byMonth), now)).toBeUndefined()
    expect(seriesFilter(report([]), series('A', byMonth, { Filter: '  ' }), now)).toBeUndefined()
  })

  it('puts the time window on the date of a windowed series', () => {
    const windowed = series('A', byMonth, { Windowed: true, WindowProperty: 'Created' })

    expect(JSON.parse(seriesFilter(report([], { TimeRange: '7d' }), windowed, now) ?? '')).toEqual({
      Logic: 'AND',
      Filters: [
        { Property: 'Created', Operator: 'greaterorequal', Value: Date.parse('2026-03-24T10:20:30.000Z') },
        { Property: 'Created', Operator: 'lessorequal', Value: now.getTime() },
      ],
    })
  })

  it('keeps the stored filter of a windowed series next to the window', () => {
    const windowed = series('A', byMonth, { Windowed: true, WindowProperty: 'Created', Filter: stored })
    const sent = JSON.parse(seriesFilter(report([], { TimeRange: '24h' }), windowed, now) ?? '') as { Filters: unknown[] }

    expect(sent.Filters).toHaveLength(3)
    expect(sent.Filters[0]).toEqual(JSON.parse(stored))
  })

  it('refuses a windowed series whose stored filter is not JSON', () => {
    const windowed = series('A', byMonth, { Windowed: true, WindowProperty: 'Created', Filter: '{oops' })

    expect(() => seriesFilter(report([], { TimeRange: '7d' }), windowed, now)).toThrow('The filter of this series cannot be read.')
  })

  it('sends no window when the stored time range cannot be read', () => {
    const windowed = series('A', byMonth, { Windowed: true, WindowProperty: 'Created' })

    expect(seriesFilter(report([], { TimeRange: 'soon' }), windowed, now)).toBeUndefined()
  })
})

describe('seriesQuery', () => {
  it('asks for the series with the Top N of the report as page size', () => {
    const paid = series('Paid', byMonth, { GroupBy: 'Created.Year, Created.Month', Property: 'Total.Sum' })

    expect(seriesQuery(report([paid], { MaxRecords: 50 }), paid, now)).toEqual({
      Entity: 'Orders',
      Properties: 'Total.Sum',
      Filter: undefined,
      GroupBy: 'Created.Year,Created.Month',
      PageIndex: 1,
      PageSize: 50,
    })
  })
})

describe('aggregateUrl', () => {
  it('is the encoded address of the call, with the application token', () => {
    const query = { Entity: 'Orders', Properties: 'ID.Count', Filter: '{"a":"b c"}', GroupBy: 'Status', PageIndex: 1, PageSize: 20 }

    expect(aggregateUrl('https://api.example.com/', 'tok', query)).toBe(
      'https://api.example.com/api/Stats/Aggregate?Entity=Orders&Properties=ID.Count&Filter=%7B%22a%22%3A%22b%20c%22%7D&GroupBy=Status&PageIndex=1&PageSize=20&AppToken=tok',
    )
  })

  it('leaves out a filter that is not there', () => {
    const query = { Entity: 'Orders', Properties: 'ID.Count', Filter: undefined, GroupBy: 'Status', PageIndex: 1, PageSize: 20 }

    expect(aggregateUrl('https://api.example.com', 'tok', query)).toBe(
      'https://api.example.com/api/Stats/Aggregate?Entity=Orders&Properties=ID.Count&GroupBy=Status&PageIndex=1&PageSize=20&AppToken=tok',
    )
  })
})

describe('panelScope', () => {
  const dated = series('By month', byMonth, { Windowed: true, WindowProperty: 'Created' })
  const plain = series('By status', [group('Status', 'String')])

  it('shows Top N when no series is windowed', () => {
    expect(panelScope(report([plain, series('By month', byMonth)]))).toEqual({
      windowLabel: undefined,
      topNLabel: 'Top 20',
      labels: ['By status', 'By month'],
    })
  })

  it('shows the time range when every series is windowed', () => {
    expect(panelScope(report([dated], { TimeRange: '7d', TimeRangeLabel: 'Last 7 days' }))).toEqual({
      windowLabel: 'Last 7 days',
      topNLabel: undefined,
      labels: ['By month'],
    })
  })

  it('says the scope of each series in its label when a panel mixes both', () => {
    expect(panelScope(report([dated, plain], { TimeRange: '7d', TimeRangeLabel: 'Last 7 days', MaxRecords: 5 }))).toEqual({
      windowLabel: undefined,
      topNLabel: undefined,
      labels: ['By month · Last 7 days', 'By status · Top 5'],
    })
  })

  it('does not count a series that cannot run', () => {
    const broken = series('Broken', [], { Error: "Entity 'Gone' does not exist", Aggregate: undefined })

    expect(panelScope(report([dated, broken], { TimeRange: '7d', TimeRangeLabel: 'Last 7 days' }))).toEqual({
      windowLabel: 'Last 7 days',
      topNLabel: undefined,
      labels: ['By month', 'Broken'],
    })
    expect(panelScope(report([broken]))).toEqual({ windowLabel: undefined, topNLabel: undefined, labels: ['Broken'] })
  })
})

describe('rowValue', () => {
  it('finds a column whatever its letter case', () => {
    expect(rowValue({ ID_count: 3 }, 'ID_count')).toBe(3)
    expect(rowValue({ ID_Count: 3 }, 'ID_count')).toBe(3)
    expect(rowValue({ id_count: 0 }, 'ID_count')).toBe(0)
    expect(rowValue({ Other: 1 }, 'ID_count')).toBeUndefined()
  })
})

describe('combineSeries', () => {
  it('labels a date by the parts it is grouped by and sorts by time', () => {
    // The API server answers newest first.
    const rows = [
      { Created_year: 2026, Created_month: 2, ID_count: 7 },
      { Created_year: 2025, Created_month: 12, ID_count: 4 },
      { Created_year: 2025, Created_month: 3, ID_count: 9 },
    ]

    expect(combineSeries([{ label: 'Orders', series: series('Orders', byMonth), rows }])).toEqual({
      labels: ['3/2025', '12/2025', '2/2026'],
      series: [{ label: 'Orders', values: [9, 4, 7] }],
    })
  })

  it('writes day, month, year, hour, minute and second without leading zeros', () => {
    const all = ['year', 'month', 'day', 'hour', 'minute', 'second'].map((part) => group('Created', 'Date', part))
    const row = { Created_year: 2026, Created_month: 3, Created_day: 9, Created_hour: 7, Created_minute: 5, Created_second: 1, ID_count: 1 }

    expect(combineSeries([{ label: 'A', series: series('A', all), rows: [row] }]).labels).toEqual(['9/3/2026 7:5:1'])
    expect(combineSeries([{ label: 'A', series: series('A', [group('Created', 'Date', 'year')]), rows: [row] }]).labels).toEqual(['2026'])
    expect(
      combineSeries([
        { label: 'A', series: series('A', [group('Created', 'Date', 'day'), group('Created', 'Date', 'hour')]), rows: [row] },
      ]).labels,
    ).toEqual(['9/7'])
  })

  it('labels any other group by its value and sorts by text', () => {
    const rows = [
      { Status: 'paid', ID_count: 5 },
      { Status: null, ID_count: 2 },
      { Status: 'new', ID_count: 1 },
    ]

    expect(combineSeries([{ label: 'A', series: series('A', [group('Status', 'String')]), rows }])).toEqual({
      labels: ['', 'new', 'paid'],
      series: [{ label: 'A', values: [2, 1, 5] }],
    })
  })

  it('joins the parts of several group-bys, the ones of one type together', () => {
    const groups = [group('Status', 'String'), group('Created', 'Date', 'year'), group('Country', 'String')]
    const rows = [{ Status: 'paid', Created_year: 2026, Country: 'GR', ID_count: 5 }]

    expect(combineSeries([{ label: 'A', series: series('A', groups), rows }]).labels).toEqual(['paid / GR / 2026'])
  })

  it('reads columns whatever their letter case', () => {
    const rows = [{ created_year: 2026, CREATED_MONTH: 1, ID_Count: 3 }]

    expect(combineSeries([{ label: 'A', series: series('A', byMonth), rows }])).toEqual({
      labels: ['1/2026'],
      series: [{ label: 'A', values: [3] }],
    })
  })

  it('puts several series on one axis, leaving a gap where a series has no group', () => {
    const paid = [
      { Created_year: 2026, Created_month: 2, ID_count: 7 },
      { Created_year: 2026, Created_month: 1, ID_count: 4 },
    ]
    const refunded = [
      { Created_year: 2026, Created_month: 3, Total_sum: 10.5 },
      { Created_year: 2026, Created_month: 1, Total_sum: 2 },
    ]
    const sum = series('Refunded', byMonth, { Property: 'Total.Sum', Aggregate: { Property: 'Total', Function: 'Sum', Column: 'Total_sum' } })

    expect(
      combineSeries([
        { label: 'Paid', series: series('Paid', byMonth), rows: paid },
        { label: 'Refunded', series: sum, rows: refunded },
      ]),
    ).toEqual({
      labels: ['1/2026', '2/2026', '3/2026'],
      series: [
        { label: 'Paid', values: [4, 7, undefined] },
        { label: 'Refunded', values: [2, undefined, 10.5] },
      ],
    })
  })

  it('sorts by text when the series do not all start with a date', () => {
    const result = combineSeries([
      { label: 'A', series: series('A', [group('Created', 'Date', 'year')]), rows: [{ Created_year: 2026, ID_count: 1 }] },
      { label: 'B', series: series('B', [group('Status', 'String')]), rows: [{ Status: 'new', ID_count: 2 }] },
    ])

    expect(result.labels).toHaveLength(2)
    expect(result.labels).toContain('2026')
    expect(result.labels).toContain('new')
  })

  it('gives a series without rows or without a resolved value no points', () => {
    const broken = series('Broken', [], { Aggregate: undefined, Error: 'gone' })

    expect(
      combineSeries([
        { label: 'A', series: series('A', [group('Status', 'String')]), rows: [] },
        { label: 'Broken', series: broken, rows: [{ Status: 'x', ID_count: 1 }] },
      ]),
    ).toEqual({ labels: [], series: [{ label: 'A', values: [] }, { label: 'Broken', values: [] }] })
  })
})

describe('hasNoRows', () => {
  it('is true when no series has a row', () => {
    const one = series('A', byMonth)

    expect(hasNoRows([{ label: 'A', series: one, rows: [] }])).toBe(true)
    expect(hasNoRows([{ label: 'A', series: one, rows: [] }, { label: 'B', series: one, rows: [{ ID_count: 1 }] }])).toBe(false)
  })
})

describe('gridText', () => {
  it('is the text of a value and nothing for a missing one', () => {
    expect(gridText(5)).toBe('5')
    expect(gridText(0)).toBe('0')
    expect(gridText('<b>x</b>')).toBe('<b>x</b>')
    expect(gridText(null)).toBe('')
    expect(gridText(undefined)).toBe('')
  })
})

describe('toNumberOrNull', () => {
  it('keeps numbers and numeric text', () => {
    expect(toNumberOrNull(3)).toBe(3)
    expect(toNumberOrNull('2.5')).toBe(2.5)
    expect(toNumberOrNull(0)).toBe(0)
  })

  it('turns anything else into null', () => {
    for (const value of [undefined, null, '', 'abc']) {
      expect(toNumberOrNull(value)).toBeNull()
    }
  })
})

describe('withAlpha', () => {
  it('turns #rrggbb into rgba', () => {
    expect(withAlpha('#4e79a7', 0.3)).toBe('rgba(78,121,167,0.3)')
  })

  it('leaves any other colour as it is', () => {
    expect(withAlpha('oklch(0.7 0.1 250)', 0.3)).toBe('oklch(0.7 0.1 250)')
  })
})

describe('chartConfig', () => {
  const combined = {
    labels: ['1/2026', '2/2026', '3/2026', '4/2026'],
    series: [
      { label: 'Paid', values: [4, '7', undefined, 'n/a'] },
      { label: 'Refunded', values: [2, undefined, 10.5, 1] },
    ],
  }

  it('draws a pie of the first series, a colour per slice', () => {
    const config = chartConfig('Pie', combined, palette)

    expect(config.type).toBe('pie')
    expect(config.data.labels).toEqual(combined.labels)
    expect(config.data.datasets).toEqual([
      { data: [4, 7, null, null], backgroundColor: ['#4e79a7', '#f28e2b', '#e15759', '#4e79a7'] },
    ])
    expect(config.options.plugins).toEqual({ legend: { display: true, position: 'right' } })
  })

  it('draws a line per series', () => {
    const config = chartConfig('Line', combined, palette)

    expect(config.type).toBe('line')
    expect(config.data.datasets).toEqual([
      {
        label: 'Paid',
        data: [4, 7, null, null],
        borderColor: '#4e79a7',
        borderWidth: 2,
        backgroundColor: '#4e79a7',
        pointBackgroundColor: '#4e79a7',
        pointRadius: 3,
        fill: false,
        spanGaps: true,
      },
      {
        label: 'Refunded',
        data: [2, null, 10.5, 1],
        borderColor: '#f28e2b',
        borderWidth: 2,
        backgroundColor: '#f28e2b',
        pointBackgroundColor: '#f28e2b',
        pointRadius: 3,
        fill: false,
        spanGaps: true,
      },
    ])
    expect(config.options.interaction).toEqual({ mode: 'index', intersect: false })
    expect(config.options.scales).toEqual({ y: { suggestedMin: 0 } })
    expect(config.options.plugins).toEqual({ legend: { display: true, position: 'bottom' } })
  })

  it('draws bars, stacked for a stacked bar', () => {
    expect(chartConfig('Bar', combined, palette).type).toBe('bar')
    expect(chartConfig('Bar', combined, palette).options.scales).toEqual({ y: { suggestedMin: 0 } })

    const stacked = chartConfig('StackedBar', combined, palette)

    expect(stacked.type).toBe('bar')
    expect(stacked.options.scales).toEqual({ x: { stacked: true }, y: { stacked: true, suggestedMin: 0 } })
  })

  it('draws a radar with see-through shapes and the nearest point in the tooltip', () => {
    const config = chartConfig('Radar', combined, palette)

    expect(config.type).toBe('radar')
    expect(config.data.datasets[0]).toMatchObject({ backgroundColor: 'rgba(78,121,167,0.3)', borderColor: '#4e79a7', pointRadius: 2, fill: true })
    expect(config.options.interaction).toEqual({ mode: 'nearest', intersect: true })
    expect(config.options.scales).toEqual({ r: { suggestedMin: 0, ticks: { display: false } } })
  })

  it('draws a stored type it does not know as lines', () => {
    expect(chartConfig('9', combined, palette).type).toBe('line')
  })

  it('uses the colours in turn', () => {
    const many = { labels: ['a'], series: ['A', 'B', 'C', 'D'].map((label) => ({ label, values: [1] })) }

    expect(chartConfig('Bar', many, palette).data.datasets.map((dataset) => dataset.borderColor)).toEqual([
      '#4e79a7',
      '#f28e2b',
      '#e15759',
      '#4e79a7',
    ])
  })
})
