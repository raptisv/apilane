import { describe, expect, it } from 'vitest'
import { allFilterOperators, filterSummary, filterText, newFilterRow, parseFilter } from './filterBuilder'

describe('newFilterRow', () => {
  it('starts at the first property with equal', () => {
    expect(newFilterRow(['ID', 'Name'])).toEqual({ property: 'ID', operator: 'equal', value: '' })
    expect(newFilterRow([])).toEqual({ property: '', operator: 'equal', value: '' })
  })
})

describe('filterText', () => {
  it('writes the JSON the classic builder writes', () => {
    expect(
      filterText([
        { property: 'Status', operator: 'equal', value: 'paid' },
        { property: 'Total', operator: 'greaterorequal', value: '10' },
      ]),
    ).toBe(
      '{"Logic":"and","Filters":[{"Property":"Status","Operator":"equal","Value":"paid"},{"Property":"Total","Operator":"greaterorequal","Value":"10"}]}',
    )
  })

  it('sends the text null as null and an empty value as empty text', () => {
    expect(
      filterText([
        { property: 'Owner', operator: 'equal', value: 'null' },
        { property: 'Name', operator: 'notequal', value: '' },
      ]),
    ).toBe(
      '{"Logic":"and","Filters":[{"Property":"Owner","Operator":"equal","Value":null},{"Property":"Name","Operator":"notequal","Value":""}]}',
    )
  })

  it('is empty for no condition', () => {
    expect(filterText([])).toBe('')
  })
})

describe('parseFilter', () => {
  it('reads no filter as no conditions', () => {
    expect(parseFilter(null)).toEqual({ rows: [] })
    expect(parseFilter(undefined)).toEqual({ rows: [] })
    expect(parseFilter('  ')).toEqual({ rows: [] })
  })

  it('reads back what filterText wrote', () => {
    const rows = [
      { property: 'Status', operator: 'contains', value: 'New York' },
      { property: 'Owner', operator: 'equal', value: 'null' },
    ]

    expect(parseFilter(filterText(rows))).toEqual({ rows })
  })

  it('reads names and operators whatever their letter case, and the other names of an operator', () => {
    expect(parseFilter('{"logic":"AND","filters":[{"property":"Total","operator":">=","value":5},{"Property":"Paid","Operator":"EQ","Value":true}]}')).toEqual({
      rows: [
        { property: 'Total', operator: 'greaterorequal', value: '5' },
        { property: 'Paid', operator: 'equal', value: 'true' },
      ],
    })
  })

  it('reads a filter without Logic as AND, and a single OR condition', () => {
    expect(parseFilter('{"Filters":[{"Property":"A","Operator":"equal","Value":"1"}]}').rows).toHaveLength(1)
    expect(parseFilter('{"Logic":"OR","Filters":[{"Property":"A","Operator":"equal","Value":"1"}]}').rows).toHaveLength(1)
  })

  it('keeps as text what the builder cannot show', () => {
    const cases = [
      '{oops',
      '"text"',
      '[]',
      '{"Property":"A","Operator":"equal","Value":"1"}',
      '{"Logic":"or","Filters":[{"Property":"A","Operator":"equal","Value":"1"},{"Property":"B","Operator":"equal","Value":"2"}]}',
      '{"Logic":"and","Filters":[{"Logic":"or","Filters":[{"Property":"A","Operator":"equal","Value":"1"}]}]}',
      '{"Logic":"and","Filters":[{"Property":"A","Operator":"between","Value":"1"}]}',
      '{"Logic":"and","Filters":[{"Property":"A","Operator":"equal","Value":{"x":1}}]}',
      '{"Logic":"and","Filters":[{"Operator":"equal","Value":"1"}]}',
    ]

    for (const text of cases) {
      expect(parseFilter(text)).toEqual({ raw: text })
    }
  })
})

describe('filterSummary', () => {
  it('says how many conditions a filter has', () => {
    expect(filterSummary('')).toBe('No filter')
    expect(filterSummary(filterText([{ property: 'A', operator: 'equal', value: '1' }]))).toBe('1 condition')
    expect(
      filterSummary(
        filterText([
          { property: 'A', operator: 'equal', value: '1' },
          { property: 'B', operator: 'less', value: '2' },
        ]),
      ),
    ).toBe('2 conditions')
    expect(filterSummary('{oops')).toBe('Custom filter')
  })
})

describe('allFilterOperators', () => {
  it('lists the ten operators of the data API', () => {
    expect(allFilterOperators).toHaveLength(10)
  })
})
