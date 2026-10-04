import { describe, expect, it } from 'vitest'
import { changedItems, changesText, newRule, restoreRule, rulesRequest, setRule, toEditable } from './security'
import type { SecurityItem, SecurityRule } from './securityAccess'

const customers: SecurityItem = {
  Type: 'Entity',
  Name: 'Customers',
  IsSystem: false,
  AllowPost: true,
  AllowPut: true,
  AllowDelete: true,
  HasOwner: true,
  PropertiesGet: ['Name', 'Email'],
  PropertiesPostPut: ['Name'],
}

const orders: SecurityItem = { ...customers, Name: 'Orders' }

function rule(roleId: string, action: string, changes: Partial<SecurityRule> = {}): SecurityRule {
  return { ...newRule(customers, roleId, action), ...changes }
}

describe('toEditable', () => {
  it('turns a missing rate limit into null and copies the properties', () => {
    const properties = ['Name']
    const editable = toEditable({ Type: 'Entity', Name: 'Customers', RoleID: 'ANONYMOUS', Action: 'get', Record: 'All', Properties: properties })

    expect(editable.RateLimit).toBeNull()
    expect(editable.Properties).toEqual(['Name'])
    expect(editable.Properties).not.toBe(properties)
  })
})

describe('newRule', () => {
  it('starts with every record, no properties and no rate limit', () => {
    expect(newRule(customers, 'Admin', 'put')).toEqual({
      Type: 'Entity',
      Name: 'Customers',
      RoleID: 'Admin',
      Action: 'put',
      Record: 'All',
      Properties: [],
      RateLimit: null,
    })
  })
})

describe('setRule', () => {
  it('adds, replaces and removes the rule of one cell', () => {
    const added = setRule([], customers, 'Admin', 'get', rule('Admin', 'get'))
    expect(added).toHaveLength(1)

    const replaced = setRule(added, customers, 'Admin', 'get', rule('Admin', 'get', { Record: 'Owned' }))
    expect(replaced).toEqual([rule('Admin', 'get', { Record: 'Owned' })])

    expect(setRule(replaced, customers, 'Admin', 'get', undefined)).toEqual([])
  })

  it('leaves the other cells alone', () => {
    const rules = [rule('Admin', 'get'), rule('Admin', 'post')]

    expect(setRule(rules, customers, 'Admin', 'post', undefined)).toEqual([rule('Admin', 'get')])
  })
})

describe('restoreRule', () => {
  it('brings back the properties and the rate limit of a cell switched off, with every record', () => {
    const kept = rule('Admin', 'get', { Record: 'Owned', Properties: ['Name'], RateLimit: { MaxRequests: 5, TimeWindow: 'Per_Minute' } })
    const restored = restoreRule(newRule(customers, 'Admin', 'get'), kept)

    expect(restored).toEqual(rule('Admin', 'get', { Properties: ['Name'], RateLimit: { MaxRequests: 5, TimeWindow: 'Per_Minute' } }))
    expect(restored.Properties).not.toBe(kept.Properties)
    expect(restored.RateLimit).not.toBe(kept.RateLimit)
  })

  it('keeps a remembered rule without a rate limit that way', () => {
    expect(restoreRule(newRule(customers, 'Admin', 'get'), rule('Admin', 'get', { Properties: ['Email'] })).RateLimit).toBeNull()
  })

  it('returns the new rule when nothing was remembered', () => {
    const fresh = newRule(customers, 'Admin', 'get')

    expect(restoreRule(fresh, undefined)).toBe(fresh)
  })
})

describe('rulesRequest', () => {
  it('leaves out an empty rate limit and sends an empty number box as 0', () => {
    const body = rulesRequest([rule('Admin', 'get'), rule('Admin', 'put', { RateLimit: { MaxRequests: '', TimeWindow: 'Per_Minute' } })])

    expect(body.Rules?.[0]?.RateLimit).toBeUndefined()
    expect(body.Rules?.[1]?.RateLimit).toEqual({ MaxRequests: 0, TimeWindow: 'Per_Minute' })
  })
})

describe('changedItems', () => {
  it('finds nothing when the rules are the same in another order', () => {
    const saved = [rule('Admin', 'get', { Properties: ['Name', 'Email'] }), rule('Admin', 'post')]
    const edited = [rule('Admin', 'post'), rule('Admin', 'get', { Properties: ['Email', 'Name'] })]

    expect(changedItems(saved, edited).size).toBe(0)
  })

  it('names the items with an added, removed or changed rule', () => {
    const saved = [rule('Admin', 'get'), { ...newRule(orders, 'Admin', 'get') }]
    const edited = [rule('Admin', 'get', { RateLimit: { MaxRequests: 5, TimeWindow: 'Per_Second' } })]

    expect([...changedItems(saved, edited)].sort()).toEqual(['Entity-Customers', 'Entity-Orders'])
  })
})

describe('changesText', () => {
  it('counts the items', () => {
    expect(changesText(1)).toBe('Unsaved rule changes in 1 item.')
    expect(changesText(3)).toBe('Unsaved rule changes in 3 items.')
  })
})
