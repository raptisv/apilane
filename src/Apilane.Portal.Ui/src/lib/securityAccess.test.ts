import { describe, expect, it } from 'vitest'
import {
  ANONYMOUS,
  AUTHENTICATED,
  describeAccess,
  effectiveAccess,
  endpointRateLimit,
  inheritance,
  itemActions,
  itemKey,
  offeredProperties,
  ownedApplies,
  propertyAccess,
  rateLimitShort,
  rateLimitText,
  rolesThatApply,
} from './securityAccess'
import type { SecurityItem, SecurityRule } from './securityAccess'

const customers: SecurityItem = {
  Type: 'Entity',
  Name: 'Customers',
  IsSystem: false,
  AllowPost: true,
  AllowPut: true,
  AllowDelete: true,
  HasOwner: true,
  PropertiesGet: ['Name', 'Email', 'Owner'],
  PropertiesPostPut: ['Name', 'Email'],
}

const noOwner: SecurityItem = { ...customers, Name: 'Countries', HasOwner: false }

const schema: SecurityItem = {
  Type: 'Schema',
  Name: 'Schema',
  IsSystem: false,
  AllowPost: false,
  AllowPut: false,
  AllowDelete: false,
  HasOwner: false,
  PropertiesGet: [],
  PropertiesPostPut: [],
}

function rule(roleId: string, action: string, changes: Partial<SecurityRule> = {}): SecurityRule {
  return {
    Type: 'Entity',
    Name: 'Customers',
    RoleID: roleId,
    Action: action,
    Record: 'All',
    Properties: ['Name', 'Email', 'Owner'],
    RateLimit: null,
    ...changes,
  }
}

describe('itemKey', () => {
  it('is the type and the name, as ?item= holds them', () => {
    expect(itemKey(customers)).toBe('Entity-Customers')
    expect(itemKey(schema)).toBe('Schema-Schema')
  })
})

describe('itemActions and offeredProperties', () => {
  it('lists get and only the actions the item allows', () => {
    expect(itemActions(customers)).toEqual(['get', 'post', 'put', 'delete'])
    expect(itemActions(schema)).toEqual(['get'])
    expect(itemActions({ ...customers, AllowPost: false })).toEqual(['get', 'put', 'delete'])
  })

  it('offers the get list for get, the editable list for post and put, nothing for delete', () => {
    expect(offeredProperties(customers, 'get')).toEqual(['Name', 'Email', 'Owner'])
    expect(offeredProperties(customers, 'PUT')).toEqual(['Name', 'Email'])
    expect(offeredProperties(customers, 'delete')).toEqual([])
  })
})

describe('ownedApplies', () => {
  it('needs an Owner column', () => {
    expect(ownedApplies(customers, AUTHENTICATED, 'get')).toBe(true)
    expect(ownedApplies(noOwner, AUTHENTICATED, 'get')).toBe(false)
  })

  it('does nothing for anonymous callers or for a post', () => {
    expect(ownedApplies(customers, ANONYMOUS, 'get')).toBe(false)
    expect(ownedApplies(customers, 'Admin', 'post')).toBe(false)
  })
})

describe('rolesThatApply', () => {
  it('gives every role the rules of Anonymous and Authenticated', () => {
    expect(rolesThatApply(ANONYMOUS)).toEqual([ANONYMOUS])
    expect(rolesThatApply(AUTHENTICATED)).toEqual([ANONYMOUS, AUTHENTICATED])
    expect(rolesThatApply('Admin')).toEqual([ANONYMOUS, AUTHENTICATED, 'Admin'])
  })
})

describe('inheritance', () => {
  it('marks every other role when Anonymous has the cell, even with its own rule', () => {
    const rules = [rule(ANONYMOUS, 'get'), rule('Admin', 'get')]

    expect(inheritance(rules, customers, ANONYMOUS, 'get')).toBeUndefined()
    expect(inheritance(rules, customers, AUTHENTICATED, 'get')).toBe('anonymous')
    expect(inheritance(rules, customers, 'Admin', 'get')).toBe('anonymous')
  })

  it('marks a role without its own rule when Authenticated has the cell', () => {
    const rules = [rule(AUTHENTICATED, 'get'), rule('Admin', 'get')]

    expect(inheritance(rules, customers, 'Editor', 'get')).toBe('authenticated')
    expect(inheritance(rules, customers, 'Admin', 'get')).toBeUndefined()
    expect(inheritance(rules, customers, AUTHENTICATED, 'get')).toBeUndefined()
    expect(inheritance(rules, customers, 'Editor', 'post')).toBeUndefined()
  })

  it('compares the action ignoring case', () => {
    expect(inheritance([rule(AUTHENTICATED, 'GET')], customers, 'Editor', 'get')).toBe('authenticated')
  })
})

describe('propertyAccess', () => {
  it('reads an empty list as no properties, not as all of them', () => {
    expect(propertyAccess(customers, 'get', [])).toEqual({ kind: 'none', included: [], excluded: ['Name', 'Email', 'Owner'] })
  })

  it('tells all from some, in the order the item offers them, ignoring case', () => {
    expect(propertyAccess(customers, 'put', ['email', 'Name']).kind).toBe('all')
    expect(propertyAccess(customers, 'get', ['Owner', 'Name'])).toEqual({
      kind: 'some',
      included: ['Name', 'Owner'],
      excluded: ['Email'],
    })
  })

  it('has nothing to choose for delete, the schema and custom endpoints', () => {
    expect(propertyAccess(customers, 'delete', []).kind).toBe('fixed')
    expect(propertyAccess(schema, 'get', []).kind).toBe('fixed')
  })
})

describe('rate limit labels', () => {
  it('writes the short and the long form', () => {
    expect(rateLimitShort({ MaxRequests: 10, TimeWindow: 'Per_Second' })).toBe('10/sec')
    expect(rateLimitShort({ MaxRequests: 5, TimeWindow: 'Per_Minute' })).toBe('5/min')
    expect(rateLimitShort({ MaxRequests: 100, TimeWindow: 'Per_Hour' })).toBe('100/hr')
    expect(rateLimitText({ MaxRequests: 1, TimeWindow: 'Per_Minute' })).toBe('1 request per minute')
    expect(rateLimitText({ MaxRequests: 20, TimeWindow: 'Per_Hour' })).toBe('20 requests per hour')
  })
})

describe('endpointRateLimit', () => {
  const perMinute = (n: number) => ({ MaxRequests: n, TimeWindow: 'Per_Minute' })

  it('is none when a rule that applies has no limit', () => {
    const rules = [rule(ANONYMOUS, 'get', { RateLimit: perMinute(5) }), rule(AUTHENTICATED, 'get')]

    expect(endpointRateLimit(rules, customers, 'Admin', 'get')).toBeNull()
  })

  it('takes the most generous limit of the rules that apply', () => {
    const rules = [
      rule(ANONYMOUS, 'get', { RateLimit: { MaxRequests: 100, TimeWindow: 'Per_Hour' } }),
      rule('Admin', 'get', { RateLimit: perMinute(2) }),
      rule('Admin', 'post', { RateLimit: { MaxRequests: 50, TimeWindow: 'Per_Second' } }),
    ]

    expect(endpointRateLimit(rules, customers, 'Admin', 'get')).toEqual(perMinute(2))
  })

  it('does not let the rule of another role change the limit of a caller', () => {
    const rules = [rule(ANONYMOUS, 'get', { RateLimit: { MaxRequests: 100, TimeWindow: 'Per_Hour' } }), rule('Admin', 'get')]

    expect(endpointRateLimit(rules, customers, ANONYMOUS, 'get')).toEqual({ MaxRequests: 100, TimeWindow: 'Per_Hour' })
    expect(endpointRateLimit(rules, customers, 'Editor', 'get')).toEqual({ MaxRequests: 100, TimeWindow: 'Per_Hour' })
    expect(endpointRateLimit(rules, customers, 'Admin', 'get')).toBeNull()
  })

  it('does not mix an entity with a custom endpoint of the same name', () => {
    const endpoint: SecurityItem = { ...customers, Type: 'CustomEndpoint' }
    const rules = [rule(ANONYMOUS, 'get', { RateLimit: perMinute(5) }), rule(ANONYMOUS, 'get', { Type: 'CustomEndpoint' })]

    expect(endpointRateLimit(rules, customers, ANONYMOUS, 'get')).toEqual(perMinute(5))
    expect(endpointRateLimit(rules, endpoint, ANONYMOUS, 'get')).toBeNull()
  })

  it('is none without rules', () => {
    expect(endpointRateLimit([], customers, ANONYMOUS, 'get')).toBeNull()
  })
})

describe('effectiveAccess', () => {
  it('gives a role what Authenticated has, marked as inherited', () => {
    const access = effectiveAccess([rule(AUTHENTICATED, 'get')], customers, 'Editor', 'get')

    expect(access.allowed).toBe(true)
    expect(access.inherited).toBe(true)
    expect(access.from).toEqual([AUTHENTICATED])
    expect(access.full).toBe(true)
  })

  it('does not give Anonymous what Authenticated has', () => {
    expect(effectiveAccess([rule(AUTHENTICATED, 'get')], customers, ANONYMOUS, 'get').allowed).toBe(false)
  })

  it('adds up the properties of the rules that apply', () => {
    const rules = [rule(AUTHENTICATED, 'get', { Properties: ['Name'] }), rule('Editor', 'get', { Properties: ['Email'] })]
    const access = effectiveAccess(rules, customers, 'Editor', 'get')

    expect(access.inherited).toBe(false)
    expect(access.properties).toEqual({ kind: 'some', included: ['Name', 'Email'], excluded: ['Owner'] })
    expect(access.full).toBe(false)
  })

  it('is not full access with no properties', () => {
    const access = effectiveAccess([rule('Editor', 'put', { Properties: [] })], customers, 'Editor', 'put')

    expect(access.properties.kind).toBe('none')
    expect(access.full).toBe(false)
    expect(describeAccess(access)).toEqual(['No properties: the call is refused'])
  })

  it('limits to owned records when any rule that applies does, only with an Owner column', () => {
    const rules = [rule(AUTHENTICATED, 'get', { Record: 'Owned' }), rule('Admin', 'get')]

    expect(effectiveAccess(rules, customers, 'Admin', 'get').owned).toBe(true)
    expect(effectiveAccess(rules, customers, 'Admin', 'get').full).toBe(false)
    expect(effectiveAccess(rules.map((r) => ({ ...r, Name: 'Countries' })), noOwner, 'Admin', 'get').owned).toBe(false)
  })

  it('ignores owned records for anonymous callers', () => {
    expect(effectiveAccess([rule(ANONYMOUS, 'get', { Record: 'Owned' })], customers, ANONYMOUS, 'get').owned).toBe(false)
  })

  it('carries the rate limit of the rules that apply', () => {
    const rules = [rule(ANONYMOUS, 'get', { RateLimit: { MaxRequests: 3, TimeWindow: 'Per_Second' } })]
    const access = effectiveAccess(rules, customers, 'Admin', 'get')

    expect(access.rateLimit).toEqual({ MaxRequests: 3, TimeWindow: 'Per_Second' })
    expect(describeAccess(access)).toEqual(['Full access', 'Rate limit 3/sec'])
  })
})

describe('describeAccess', () => {
  it('lists the restrictions', () => {
    const rules = [rule('Admin', 'get', { Record: 'Owned', Properties: ['Name'] })]

    expect(describeAccess(effectiveAccess(rules, customers, 'Admin', 'get'))).toEqual([
      'Owned records only',
      'Included: Name',
      'Excluded: Email, Owner',
    ])
  })

  it('says when there is no access', () => {
    expect(describeAccess(effectiveAccess([], customers, 'Admin', 'get'))).toEqual(['No access'])
  })
})
