import { describe, expect, it } from 'vitest'
import { comparisonView, constraintTypeName, isIdentical, propertyFacts, ruleText } from './comparison'
import type { ComparedProperty, ComparedRule, Comparison } from './comparison'

function comparison(parts: Partial<Pick<Comparison, 'Entities' | 'CustomEndpoints' | 'Security'>> = {}): Comparison {
  return {
    ApplicationSource: 'Shop',
    ApplicationTarget: 'Shop test',
    Entities: { Added: [], Removed: [], Changed: [] },
    CustomEndpoints: { Added: [], Removed: [], Changed: [] },
    Security: { Added: [], Removed: [], Changed: [] },
    ...parts,
  }
}

function property(name: string, values: Partial<ComparedProperty> = {}): ComparedProperty {
  return { Name: name, TypeLabel: 'String', TypeID: 1, Required: false, Encrypted: false, ...values }
}

function rule(values: Partial<ComparedRule> = {}): ComparedRule {
  return {
    Name: 'Entity Orders - ANONYMOUS get',
    Role: 'ANONYMOUS',
    Type: 'Entity',
    Action: 'get',
    Record: 'All',
    Properties: 'Code,Total',
    RateLimit: null,
    ...values,
  }
}

describe('comparisonView', () => {
  it('is identical when every list of the answer is empty', () => {
    expect(isIdentical(comparisonView(comparison()))).toBe(true)
  })

  it('keeps added and removed entities, custom endpoints and rules as they are', () => {
    const entity = { Name: 'Orders', RequireChangeTracking: false, HasDifferentiationProperty: false, Properties: [], Constraints: [] }
    const endpoint = { Name: 'GetOrders', Query: 'SELECT 1' }

    const view = comparisonView(
      comparison({
        Entities: { Added: [entity], Removed: [], Changed: [] },
        CustomEndpoints: { Added: [], Removed: [endpoint], Changed: [] },
        Security: { Added: [rule()], Removed: [], Changed: [] },
      }),
    )

    expect(view.entities.added).toEqual([entity])
    expect(view.customEndpoints.removed).toEqual([endpoint])
    expect(view.security.added).toEqual([rule()])
    expect(isIdentical(view)).toBe(false)
  })

  it('moves the properties and constraints of a changed entity into their own sections', () => {
    const view = comparisonView(
      comparison({
        Entities: {
          Added: [],
          Removed: [],
          Changed: [
            {
              Name: 'Orders',
              MetadataChanges: [],
              PropertiesAdded: [property('Phone')],
              PropertiesChanged: [{ Name: 'Code', Changes: [{ Field: 'Maximum', Before: '10', After: '20' }] }],
              PropertiesRemoved: [{ Name: 'Fax', TypeLabel: 'String' }],
              ConstraintsAdded: [{ TypeID: 1, Properties: 'Code' }],
              ConstraintsRemoved: [{ TypeID: 2, Properties: 'Customer_ID,Customers' }],
            },
          ],
        },
      }),
    )

    // Its own values are the same, so the entity is not listed under Entities.
    expect(view.entities.changed).toEqual([])
    expect(view.properties.added).toEqual([{ entity: 'Orders', item: property('Phone') }])
    expect(view.properties.removed).toEqual([{ entity: 'Orders', item: { Name: 'Fax', TypeLabel: 'String' } }])
    expect(view.properties.changed).toEqual([
      { name: 'Orders.Code', changes: [{ field: 'Maximum', before: '10', after: '20' }] },
    ])
    expect(view.constraints.added).toEqual([{ entity: 'Orders', item: { TypeID: 1, Properties: 'Code' } }])
    expect(view.constraints.removed).toEqual([{ entity: 'Orders', item: { TypeID: 2, Properties: 'Customer_ID,Customers' } }])
    expect(isIdentical(view)).toBe(false)
  })

  it('lists a changed entity under Entities when its own values differ', () => {
    const view = comparisonView(
      comparison({
        Entities: {
          Added: [],
          Removed: [],
          Changed: [
            {
              Name: 'Orders',
              MetadataChanges: [{ Field: 'Description', Before: null, After: 'Customer orders' }],
              PropertiesAdded: [],
              PropertiesChanged: [],
              PropertiesRemoved: [],
              ConstraintsAdded: [],
              ConstraintsRemoved: [],
            },
          ],
        },
      }),
    )

    expect(view.entities.changed).toEqual([
      { name: 'Orders', changes: [{ field: 'Description', before: null, after: 'Customer orders' }] },
    ])
    expect(view.properties).toEqual({ added: [], removed: [], changed: [] })
  })

  it('shows of a changed custom endpoint only what differs, the query as code', () => {
    const view = comparisonView(
      comparison({
        CustomEndpoints: {
          Added: [],
          Removed: [],
          Changed: [
            { Name: 'GetOrders', DescriptionBefore: 'All', DescriptionAfter: 'All', QueryBefore: 'SELECT 1', QueryAfter: 'SELECT 2' },
            { Name: 'GetItems', DescriptionBefore: null, DescriptionAfter: 'Items', QueryBefore: 'SELECT 3', QueryAfter: 'SELECT 3' },
          ],
        },
      }),
    )

    expect(view.customEndpoints.changed).toEqual([
      { name: 'GetOrders', changes: [{ field: 'Query', before: 'SELECT 1', after: 'SELECT 2', code: true }] },
      { name: 'GetItems', changes: [{ field: 'Description', before: null, after: 'Items' }] },
    ])
  })

  it('shows of a changed security rule only what differs, a missing rate limit as none', () => {
    const view = comparisonView(
      comparison({
        Security: {
          Added: [],
          Removed: [],
          Changed: [
            {
              Name: 'Entity Orders - ANONYMOUS get',
              SecurityBefore: rule(),
              SecurityAfter: rule({ Record: 'Owned', RateLimit: '10 request per minute' }),
            },
          ],
        },
      }),
    )

    expect(view.security.changed).toEqual([
      {
        name: 'Entity Orders - ANONYMOUS get',
        changes: [
          { field: 'Record', before: 'All', after: 'Owned' },
          { field: 'Rate limit', before: 'none', after: '10 request per minute' },
        ],
      },
    ])
  })
})

describe('ruleText', () => {
  it('joins role, action and record', () => {
    expect(ruleText(rule())).toBe('ANONYMOUS · get · All')
  })

  it('adds the rate limit when the rule has one', () => {
    expect(ruleText(rule({ RateLimit: '5 request per second' }))).toBe('ANONYMOUS · get · All · limit: 5 request per second')
  })
})

describe('propertyFacts', () => {
  it('has nothing for a property with no value set', () => {
    expect(propertyFacts(property('Title'))).toEqual([])
  })

  it('lists every value that is set, in a fixed order', () => {
    expect(
      propertyFacts(
        property('Title', { Required: true, Minimum: 0, Maximum: 200, DecimalPlaces: 2, Encrypted: true, ValidationRegex: '^[a-z]+$' }),
      ),
    ).toEqual(['Required', 'Min 0', 'Max 200', '2 decimals', 'Encrypted', 'Regex ^[a-z]+$'])
  })

  it('keeps a zero', () => {
    expect(propertyFacts(property('Count', { Minimum: 0, DecimalPlaces: 0 }))).toEqual(['Min 0', '0 decimals'])
  })
})

describe('constraintTypeName', () => {
  it('names the two kinds and shows another number as it is', () => {
    expect(constraintTypeName(1)).toBe('Unique')
    expect(constraintTypeName(2)).toBe('Foreign key')
    expect(constraintTypeName(7)).toBe('Type 7')
  })
})
