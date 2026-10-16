import { describe, expect, it } from 'vitest'
import { itemSummary, propertySummary } from './accessSummary'
import type { SecurityItem, SecurityRole, SecurityRule } from './securityAccess'

const roles: SecurityRole[] = [
  { RoleID: 'ANONYMOUS', DisplayName: 'Anonymous users', Kind: 'Anonymous', Orphaned: false },
  { RoleID: 'AUTHENTICATED', DisplayName: 'Authenticated users', Kind: 'Authenticated', Orphaned: false },
  { RoleID: 'admin', DisplayName: 'admin', Kind: 'Role', Orphaned: false },
] as SecurityRole[]

const product: SecurityItem = {
  Type: 'Entity',
  Name: 'Product',
  IsSystem: false,
  AllowPost: true,
  AllowPut: true,
  AllowDelete: true,
  HasOwner: true,
  PropertiesGet: ['ID', 'Title', 'Price', 'Secret'],
  PropertiesPostPut: ['Title', 'Price', 'Secret'],
  DifferentiationProperty: null,
} as SecurityItem

const endpoint: SecurityItem = {
  Type: 'CustomEndpoint',
  Name: 'Report',
  IsSystem: false,
  AllowPost: false,
  AllowPut: false,
  AllowDelete: false,
  HasOwner: false,
  PropertiesGet: [],
  PropertiesPostPut: [],
  DifferentiationProperty: null,
} as SecurityItem

function rule(item: SecurityItem, roleId: string, action: string, properties: string[] = [], extra: Partial<SecurityRule> = {}): SecurityRule {
  return { Type: item.Type, Name: item.Name, RoleID: roleId, Action: action, Record: 'All', Properties: properties, RateLimit: null, ...extra }
}

describe('itemSummary', () => {
  it('is closed without a rule, with a line per offered action that nobody may call', () => {
    const result = itemSummary(product, [], roles)

    expect(result.kind).toBe('closed')
    expect(result.lines.map((line) => line.action)).toEqual(['get', 'post', 'put', 'delete'])
    expect(result.lines.every((line) => line.who.length === 0)).toBe(true)
  })

  it('offers only GET for a custom endpoint', () => {
    expect(itemSummary(endpoint, [], roles).lines.map((line) => line.action)).toEqual(['get'])
  })

  it('is public when Anonymous may read', () => {
    expect(itemSummary(product, [rule(product, 'ANONYMOUS', 'get', ['Title'])], roles).kind).toBe('public')
  })

  it('is public with a write when Anonymous may post, put or delete', () => {
    for (const action of ['post', 'put', 'delete']) {
      expect(itemSummary(product, [rule(product, 'ANONYMOUS', action, ['Title'])], roles).kind).toBe('publicWrite')
    }
  })

  it('is signed-in for Authenticated alone, and roles for a named role alone', () => {
    expect(itemSummary(product, [rule(product, 'AUTHENTICATED', 'get', ['Title'])], roles).kind).toBe('signedIn')
    expect(itemSummary(product, [rule(product, 'admin', 'get', ['Title'])], roles).kind).toBe('roles')
  })

  it('takes the rules of the item only, not those of another with the same name', () => {
    const other = { ...product, Type: 'CustomEndpoint' } as SecurityItem

    expect(itemSummary(product, [rule(other, 'ANONYMOUS', 'get')], roles).kind).toBe('closed')
  })

  it('lists the roles of an action, Anonymous first, with what restricts each', () => {
    const rules = [
      rule(product, 'admin', 'get', ['ID', 'Title', 'Price', 'Secret']),
      rule(product, 'ANONYMOUS', 'get', ['Title', 'Price'], { RateLimit: { MaxRequests: 100, TimeWindow: 'Per_Minute' } }),
      rule(product, 'AUTHENTICATED', 'get', ['Title'], { Record: 'Owned' }),
    ]

    const get = itemSummary(product, rules, roles).lines[0].who

    expect(get.map((who) => who.name)).toEqual(['Anonymous users', 'Authenticated users', 'admin'])
    expect(get[0].notes).toEqual(['2 of 4 properties', '100/min'])
    expect(get[1].notes).toEqual(['1 of 4 properties', 'own records'])
    expect(get[2].notes).toEqual([])
  })

  it('says what an empty property list does', () => {
    const rules = [rule(product, 'admin', 'get', []), rule(product, 'admin', 'post', [])]
    const [get, post] = itemSummary(product, rules, roles).lines

    expect(get.who[0].notes).toEqual(['only the ID'])
    expect(post.who[0].notes).toEqual(['refused: no properties'])
  })
})

describe('propertySummary', () => {
  const rules = [
    rule(product, 'ANONYMOUS', 'get', ['Title']),
    rule(product, 'AUTHENTICATED', 'get', ['Title', 'Price']),
    rule(product, 'admin', 'get', ['Title', 'Price', 'Secret']),
    rule(product, 'admin', 'put', ['Price']),
  ]

  it('is public when Anonymous may read the property', () => {
    expect(propertySummary(product, 'Title', rules, roles).kind).toBe('public')
  })

  it('is signed-in when only Authenticated lists it', () => {
    expect(propertySummary(product, 'Price', rules.slice(1), roles).kind).toBe('signedIn')
  })

  it('is for roles when only a named role lists it, with a line per action that offers it', () => {
    const result = propertySummary(product, 'Secret', rules, roles)

    expect(result.kind).toBe('roles')
    expect(result.lines.map((line) => line.action)).toEqual(['get', 'post', 'put'])
    expect(result.lines[0].who.map((who) => who.name)).toEqual(['admin'])
    expect(result.lines[1].who).toEqual([])
  })

  it('is closed when no rule lists it, whatever the other properties give', () => {
    const result = propertySummary(product, 'Secret', rules.slice(0, 2), roles)

    expect(result.kind).toBe('closed')
    expect(result.hint).toContain('No rule lists')
  })

  it('is public with a write when Anonymous may post the property', () => {
    expect(propertySummary(product, 'Title', [rule(product, 'ANONYMOUS', 'post', ['Title'])], roles).kind).toBe('publicWrite')
  })

  it('returns the ID to every role that may read, even with an empty list', () => {
    expect(propertySummary(product, 'ID', [rule(product, 'ANONYMOUS', 'get', [])], roles).kind).toBe('public')
  })

  it('returns the ID to every role that may read, even where the offered properties do not list it', () => {
    const withoutId = { ...product, PropertiesGet: ['Title'] } as SecurityItem

    expect(propertySummary(withoutId, 'ID', [rule(withoutId, 'ANONYMOUS', 'get', ['Title'])], roles).kind).toBe('public')
  })

  it('is closed and says so for a property the API never offers', () => {
    const result = propertySummary(product, 'Created', rules, roles)

    expect(result.kind).toBe('closed')
    expect(result.lines).toEqual([])
    expect(result.hint).toContain('never returns or accepts')
  })

  it('compares the names without regard to case', () => {
    expect(propertySummary(product, 'title', rules, roles).kind).toBe('public')
  })
})
