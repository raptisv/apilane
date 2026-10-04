import { describe, expect, it } from 'vitest'
import { describeChange, jsonLinkText, valueHeaders } from './auditDiff'
import type { ChangeView } from './auditDiff'

/** Narrows a view to the kind a test expects, and fails the test when it is another kind. */
function as<K extends ChangeView['kind']>(view: ChangeView, kind: K): Extract<ChangeView, { kind: K }> {
  expect(view.kind).toBe(kind)

  return view as Extract<ChangeView, { kind: K }>
}

describe('valueHeaders', () => {
  it('depends on the action alone', () => {
    expect(valueHeaders('Modified')).toEqual(['Old Value', 'New Value'])
    expect(valueHeaders('Created')).toEqual(['Value'])
    expect(valueHeaders('Deleted')).toEqual(['Previous Value'])
  })
})

describe('jsonLinkText', () => {
  it('uses the singular for one item', () => {
    expect(jsonLinkText(1)).toBe('View JSON (1 item)')
    expect(jsonLinkText(3)).toBe('View JSON (3 items)')
    expect(jsonLinkText(0)).toBe('View JSON (0 items)')
  })
})

describe('describeChange: plain properties', () => {
  it('shows old and new value for Modified, the old one dimmed', () => {
    const view = describeChange('Modified', { Property: 'Name', OldValue: 'Shop', NewValue: 'Store' })

    expect(view).toEqual({
      kind: 'text',
      cells: [
        { text: 'Shop', old: true },
        { text: 'Store', old: false },
      ],
    })
  })

  it('shows only the new value for Created and only the old value for Deleted', () => {
    expect(describeChange('Created', { Property: 'Name', OldValue: null, NewValue: 'Shop' })).toEqual({
      kind: 'text',
      cells: [{ text: 'Shop', old: false }],
    })
    expect(describeChange('Deleted', { Property: 'Name', OldValue: 'Shop', NewValue: null })).toEqual({
      kind: 'text',
      cells: [{ text: 'Shop', old: true }],
    })
  })

  it("shows a missing value as '<null>'", () => {
    const view = as(describeChange('Modified', { Property: 'Name', NewValue: 'Store' }), 'text')

    expect(view.cells.map((cell) => cell.text)).toEqual(['<null>', 'Store'])
  })

  it('treats a JSON property under an unknown action as plain text', () => {
    const view = describeChange('Renamed', { Property: 'Security', OldValue: '[]', NewValue: '[1]' })

    expect(view).toEqual({ kind: 'text', cells: [{ text: '[]', old: true }] })
  })
})

describe('describeChange: JSON property created or deleted', () => {
  it('counts the items of the new value for Created and pretty-prints it', () => {
    const view = as(describeChange('Created', { Property: 'EntConstraints', NewValue: '[{"TypeID":1},{"TypeID":2}]' }), 'json')

    expect(view.count).toBe(2)
    expect(view.json).toBe(JSON.stringify([{ TypeID: 1 }, { TypeID: 2 }], null, 2))
  })

  it('uses the old value for Deleted', () => {
    const view = as(describeChange('Deleted', { Property: 'Security', OldValue: '[{"Name":"Users"}]', NewValue: null }), 'json')

    expect(view.count).toBe(1)
  })

  it('finds a JSON property whatever the case of its name', () => {
    expect(describeChange('Created', { Property: 'ENTDEFAULTORDER', NewValue: '[]' }).kind).toBe('json')
  })

  it('gives no items for an empty value, text that is not JSON, or JSON that is not an array', () => {
    expect(as(describeChange('Created', { Property: 'Security', NewValue: '' }), 'json')).toEqual({ kind: 'json', count: 0, json: '' })
    expect(as(describeChange('Created', { Property: 'Security', NewValue: null }), 'json').count).toBe(0)
    expect(as(describeChange('Created', { Property: 'Security', NewValue: '{"a":1}' }), 'json').count).toBe(0)

    // Text that is not JSON is shown as it is stored.
    expect(as(describeChange('Created', { Property: 'Security', NewValue: 'not json' }), 'json')).toEqual({
      kind: 'json',
      count: 0,
      json: 'not json',
    })
  })
})

describe('describeChange: Security modified', () => {
  const users = '{"Name":"Users","TypeID":0,"RoleID":"ANONYMOUS","Action":"get","Record":0,"Properties":"ID,Email"}'
  const orders = '{"Name":"Orders","TypeID":0,"RoleID":"AUTHENTICATED","Action":"post","Record":1,"Properties":"","RateLimit":{"MaxRequests":5,"TimeWindowType":2}}'

  it('lists a rule that only the new value has as added', () => {
    const view = as(describeChange('Modified', { Property: 'Security', OldValue: `[${users}]`, NewValue: `[${users},${orders}]` }), 'security')

    expect(view.added).toEqual([{ label: 'Orders · AUTHENTICATED · post', record: 'Owned', properties: '(none)', rateLimit: '5/min' }])
    expect(view.updated).toEqual([])
    expect(view.removed).toEqual([])
  })

  it('lists a rule that only the old value has as removed', () => {
    const view = as(describeChange('Modified', { Property: 'Security', OldValue: `[${users},${orders}]`, NewValue: `[${orders}]` }), 'security')

    expect(view.removed).toEqual([{ label: 'Users · ANONYMOUS · get', record: 'All', properties: 'ID,Email', rateLimit: 'None' }])
    expect(view.added).toEqual([])
    expect(view.updated).toEqual([])
  })

  it('gives three empty groups when nothing changed', () => {
    const view = describeChange('Modified', { Property: 'Security', OldValue: `[${users}]`, NewValue: `[${users}]` })

    expect(view).toEqual({ kind: 'security', added: [], updated: [], removed: [] })
  })

  it('matches rules by Name, TypeID, RoleID and Action, and lists the fields that differ', () => {
    const before = '{"Name":"Users","TypeID":0,"RoleID":"ANONYMOUS","Action":"get","Record":0,"Properties":"ID,Email"}'
    const after = '{"Name":"Users","TypeID":0,"RoleID":"ANONYMOUS","Action":"get","Record":1,"Properties":"id,Phone","RateLimit":{"MaxRequests":10,"TimeWindowType":1}}'

    const view = as(describeChange('Modified', { Property: 'Security', OldValue: `[${before}]`, NewValue: `[${after}]` }), 'security')

    expect(view.added).toEqual([])
    expect(view.removed).toEqual([])
    expect(view.updated).toEqual([
      {
        label: 'Users · ANONYMOUS · get',
        fields: [
          { field: 'Record', oldValue: 'All', newValue: 'Owned' },
          // Property names are compared ignoring case: 'ID' and 'id' are the same property.
          { field: 'Properties removed', oldValue: 'Email', newValue: '' },
          { field: 'Properties added', oldValue: '', newValue: 'Phone' },
          { field: 'Rate limit', oldValue: 'None', newValue: '10/sec' },
        ],
      },
    ])
  })

  it("shows '(none)' as removed when a rule gets its first properties", () => {
    const before = '{"Name":"Users","TypeID":0,"RoleID":"ANONYMOUS","Action":"get","Record":0,"Properties":""}'
    const after = '{"Name":"Users","TypeID":0,"RoleID":"ANONYMOUS","Action":"get","Record":0,"Properties":"ID"}'

    const view = as(describeChange('Modified', { Property: 'Security', OldValue: `[${before}]`, NewValue: `[${after}]` }), 'security')

    expect(view.updated[0]?.fields).toEqual([
      { field: 'Properties removed', oldValue: '(none)', newValue: '' },
      { field: 'Properties added', oldValue: '', newValue: 'ID' },
    ])
  })

  it('compares rules by their stored text: a rule rewritten with other spacing is updated, with no field lines', () => {
    const before = '{"Name":"Users","TypeID":0,"RoleID":"ANONYMOUS","Action":"get","Record":0,"Properties":"ID"}'
    const after = '{"Name":"Users", "TypeID":0, "RoleID":"ANONYMOUS", "Action":"get", "Record":0, "Properties":"ID"}'

    const view = as(describeChange('Modified', { Property: 'Security', OldValue: `[${before}]`, NewValue: `[${after}]` }), 'security')

    expect(view.updated).toEqual([{ label: 'Users · ANONYMOUS · get', fields: [] }])
  })

  it('counts the first of two rules with the same key', () => {
    const first = '{"Name":"Users","TypeID":0,"RoleID":"ANONYMOUS","Action":"get","Record":0}'
    const second = '{"Name":"Users","TypeID":0,"RoleID":"ANONYMOUS","Action":"get","Record":1}'

    const view = as(describeChange('Modified', { Property: 'Security', OldValue: '[]', NewValue: `[${first},${second}]` }), 'security')

    expect(view.added).toHaveLength(1)
    expect(view.added[0]?.record).toBe('All')
  })

  it('labels the rate limit windows', () => {
    const rule = (window: number) =>
      `[{"Name":"A","TypeID":0,"RoleID":"R","Action":"get","RateLimit":{"MaxRequests":3,"TimeWindowType":${window}}}]`
    const rateLimit = (window: number) =>
      as(describeChange('Modified', { Property: 'Security', OldValue: '[]', NewValue: rule(window) }), 'security').added[0]?.rateLimit

    expect(rateLimit(1)).toBe('3/sec')
    expect(rateLimit(2)).toBe('3/min')
    expect(rateLimit(3)).toBe('3/hr')
    expect(rateLimit(9)).toBe('3')
  })

  it('matches the property name exactly: another spelling is compared as a plain list', () => {
    const view = describeChange('Modified', { Property: 'security', OldValue: '[]', NewValue: `[${users}]` })

    expect(view.kind).toBe('list')
  })

  it('treats a value that is not JSON as an empty list', () => {
    const view = as(describeChange('Modified', { Property: 'Security', OldValue: 'oops', NewValue: `[${users}]` }), 'security')

    expect(view.added).toHaveLength(1)
    expect(view.removed).toEqual([])
  })
})

describe('describeChange: other JSON properties modified', () => {
  it('lists the items that left and the items that came, compared by their stored text', () => {
    const view = describeChange('Modified', {
      Property: 'EntDefaultOrder',
      OldValue: '[{"Property":"Created","Direction":"desc"},{"Property":"ID","Direction":"asc"}]',
      NewValue: '[{"Property":"ID","Direction":"asc"},{"Property":"Name","Direction":"asc"}]',
    })

    expect(view).toEqual({ kind: 'list', removed: ['Created desc'], added: ['Name asc'] })
  })

  it('shows an item written with other spacing as removed and added', () => {
    const view = as(
      describeChange('Modified', {
        Property: 'EntDefaultOrder',
        OldValue: '[{"Property":"ID","Direction":"asc"}]',
        NewValue: '[{"Property":"ID", "Direction":"asc"}]',
      }),
      'list',
    )

    expect(view.removed).toEqual(['ID asc'])
    expect(view.added).toEqual(['ID asc'])
  })

  it('is not confused by commas, brackets and quotes inside strings', () => {
    const view = as(
      describeChange('Modified', {
        Property: 'EntConstraints',
        OldValue: '[]',
        NewValue: '[{"TypeID":1,"Properties":"A,B"},{"TypeID":2,"Properties":"x]\\"y,{z"}]',
      }),
      'list',
    )

    expect(view.added).toEqual(['A,B', 'x]"y,{z'])
  })

  it('builds a label from the fields an item has', () => {
    const added = (item: string) =>
      as(describeChange('Modified', { Property: 'EntConstraints', OldValue: '[]', NewValue: `[${item}]` }), 'list').added[0]

    expect(added('{"Name":"Users","RoleID":"ADMIN","Action":"get","Properties":"ID","Record":1,"RateLimit":{"MaxRequests":2,"TimeWindowType":3}}')).toBe(
      'Users ADMIN get ID Record:Owned Rate:2/hr',
    )
    expect(added('{"Name":"Users","Record":0}')).toBe('Users Record:All')
    // Only the type is known.
    expect(added('{"TypeID":2}')).toBe('Type:2')
    expect(added('{"TypeID":"Unique"}')).toBe('Type:Unique')
    // Nothing known.
    expect(added('{"Other":true}')).toBe('(item)')
    expect(added('5')).toBe('(item)')
  })
})
