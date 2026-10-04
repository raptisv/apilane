import { describe, expect, it } from 'vitest'
import { ApiError } from './api'
import type { Report } from './reportData'
import {
  fieldNames,
  newReportForm,
  notOffered,
  reportErrors,
  reportForm,
  reportRequest,
  reportTypeLabel,
  splitGroupBy,
  toggleGroupBy,
} from './reports'
import type { ReportForm } from './reports'

const groupings = [
  { Name: 'Status', Subs: [] },
  { Name: 'Created', Subs: ['Year', 'Month', 'Day'] },
  { Name: 'Total', Subs: [] },
]
const offered = fieldNames(groupings)

function validation(errors: { Property: string; Message: string }[]): ApiError {
  return new ApiError(400, { Code: 'VALIDATION', Message: 'The request is not valid.', Errors: errors })
}

describe('reportTypeLabel', () => {
  it('names the types', () => {
    expect(reportTypeLabel('Grid')).toBe('Grid')
    expect(reportTypeLabel('StackedBar')).toBe('Stacked bar chart')
  })

  it('shows a stored type it does not know as it is', () => {
    expect(reportTypeLabel('9')).toBe('9')
  })
})

describe('newReportForm', () => {
  it('starts with one empty series on the first entity', () => {
    expect(newReportForm('Orders')).toEqual({
      Title: '',
      Type: 'Grid',
      MaxRecords: 20,
      TimeRange: '',
      Series: [{ Label: '', Entity: 'Orders', GroupBy: '', Property: '', Filter: '' }],
    })
  })
})

describe('reportForm', () => {
  it('copies a stored report, a missing time range and filter as empty text', () => {
    const report: Report = {
      ID: 4,
      Title: 'Orders per month',
      Type: 'Line',
      X: 0,
      Y: 0,
      Width: 6,
      Height: 4,
      MaxRecords: 50,
      TimeRange: null,
      TimeRangeLabel: null,
      DateModified: '2026-01-01T00:00:00Z',
      Series: [
        {
          Label: 'All',
          Entity: 'Orders',
          GroupBy: 'Created.Year,Created.Month',
          Property: 'ID.Count',
          Filter: null,
          Groups: [],
          Windowed: false,
        },
      ],
    }

    expect(reportForm(report)).toEqual({
      Title: 'Orders per month',
      Type: 'Line',
      MaxRecords: 50,
      TimeRange: '',
      Series: [{ Label: 'All', Entity: 'Orders', GroupBy: 'Created.Year,Created.Month', Property: 'ID.Count', Filter: '' }],
    })
  })
})

describe('reportRequest', () => {
  const form: ReportForm = {
    Title: 'Orders',
    Type: 'Bar',
    MaxRecords: 10,
    TimeRange: '',
    Series: [
      { Label: '', Entity: 'Orders', GroupBy: '', Property: '', Filter: '' },
      { Label: 'Paid', Entity: 'Orders', GroupBy: 'Status', Property: 'ID.Count', Filter: '{"Logic":"and","Filters":[]}' },
      { Label: 'Half done', Entity: 'Orders', GroupBy: '', Property: '', Filter: '' },
    ],
  }

  it('leaves out the empty rows and remembers where the others are', () => {
    const { body, rows } = reportRequest(form)

    expect(rows).toEqual([1, 2])
    expect(body).toEqual({
      Title: 'Orders',
      Type: 'Bar',
      MaxRecords: 10,
      TimeRange: null,
      Series: [
        { Label: 'Paid', Entity: 'Orders', GroupBy: 'Status', Property: 'ID.Count', Filter: '{"Logic":"and","Filters":[]}' },
        { Label: 'Half done', Entity: 'Orders', GroupBy: '', Property: '', Filter: null },
      ],
    })
  })

  it('sends the time range and an empty number box as null', () => {
    const { body } = reportRequest({ ...form, TimeRange: '7d', MaxRecords: '' })

    expect(body.TimeRange).toBe('7d')
    expect(body.MaxRecords).toBeNull()
  })
})

describe('reportErrors', () => {
  it('is empty without an error', () => {
    expect(reportErrors(undefined, [])).toEqual({ fields: {}, series: {}, message: undefined })
  })

  it('puts each problem at its field, a series problem at the row of the form', () => {
    const error = validation([
      { Property: 'Title', Message: 'Required' },
      { Property: 'MaxRecords', Message: 'Must be between 1 and 1000' },
      { Property: 'Series[0].Label', Message: 'Required' },
      { Property: 'Series[1].GroupBy', Message: "'Nope' is not offered" },
      { Property: 'Series[1].GroupBy', Message: 'A second message' },
      { Property: 'Series[1]', Message: 'Something about the row' },
    ])

    // The form had an empty first row, which was not sent: Series[0] is row 1 of the form.
    expect(reportErrors(error, [1, 2])).toEqual({
      fields: { Title: 'Required', MaxRecords: 'Must be between 1 and 1000' },
      series: {
        1: { Label: 'Required' },
        2: { GroupBy: "'Nope' is not offered", '': 'Something about the row' },
      },
      message: undefined,
    })
  })

  it('shows a problem of the series list as a whole at the list', () => {
    expect(reportErrors(validation([{ Property: 'Series', Message: 'Add at least one series' }]), []).fields).toEqual({
      Series: 'Add at least one series',
    })
  })

  it('shows what has no place above the form', () => {
    const error = validation([
      { Property: 'Series[7].Label', Message: 'No such row.' },
      { Property: 'Other', Message: 'Something else.' },
    ])

    expect(reportErrors(error, [0])).toEqual({ fields: {}, series: {}, message: 'No such row. Something else.' })
    expect(reportErrors(new Error('The Portal could not be reached.'), [0]).message).toBe('The Portal could not be reached.')
    expect(reportErrors(new ApiError(404, { Code: 'NOT_FOUND', Message: 'Report not found.' }), [0]).message).toBe('Report not found.')
  })
})

describe('fieldNames', () => {
  it('lists a field without subs by its name and one with subs once per sub', () => {
    expect(offered).toEqual(['Status', 'Created.Year', 'Created.Month', 'Created.Day', 'Total'])
    expect(fieldNames([{ Name: 'ID', Subs: ['Count'] }])).toEqual(['ID.Count'])
  })
})

describe('splitGroupBy', () => {
  it('splits at commas and drops spaces and empty parts', () => {
    expect(splitGroupBy('Created.Year, Created.Month,')).toEqual(['Created.Year', 'Created.Month'])
    expect(splitGroupBy('')).toEqual([])
  })
})

describe('toggleGroupBy', () => {
  it('adds in the order the fields are offered, not the order of ticking', () => {
    let text = toggleGroupBy(offered, '', 'Created.Month')
    text = toggleGroupBy(offered, text, 'Status')
    text = toggleGroupBy(offered, text, 'Created.Year')

    expect(text).toBe('Status,Created.Year,Created.Month')
  })

  it('removes one that is ticked', () => {
    expect(toggleGroupBy(offered, 'Status,Created.Year', 'Status')).toBe('Created.Year')
    expect(toggleGroupBy(offered, 'Status', 'Status')).toBe('')
  })

  it('keeps a part that is not offered until it is removed itself', () => {
    expect(toggleGroupBy(offered, 'Gone.Year,Status', 'Total')).toBe('Gone.Year,Status,Total')
    expect(toggleGroupBy(offered, 'Gone.Year,Status', 'Gone.Year')).toBe('Status')
  })
})

describe('notOffered', () => {
  it('lists the parts report-fields does not offer', () => {
    expect(notOffered(offered, 'Status,Gone.Year')).toEqual(['Gone.Year'])
    expect(notOffered(offered, 'Status,Created.Year')).toEqual([])
    expect(notOffered(offered, '')).toEqual([])
  })
})
