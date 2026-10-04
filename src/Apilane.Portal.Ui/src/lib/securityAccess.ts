import type { Schemas } from './api'

/**
 * What a security rule grants, worked out the way the API server enforces it
 * (EntityAccess.GetMaximum, ApplicationDataService, SecurityExtensions.IsRateLimited). The rule
 * editor, the tree view and the matrix view of the security screen all read access through this
 * module, so they never disagree. Pure functions.
 */

export type SecurityItem = Schemas['SecurityItemResponse']
export type SecurityRole = Schemas['SecurityRoleResponse']

/** The role of every caller, with or without an auth token. */
export const ANONYMOUS = 'ANONYMOUS'
/** The role of every signed-in user. */
export const AUTHENTICATED = 'AUTHENTICATED'

export type Action = 'get' | 'post' | 'put' | 'delete'

export const timeWindows = ['Per_Second', 'Per_Minute', 'Per_Hour'] as const

export interface RateLimit {
  /** A number, or '' while its box is empty in the editor. */
  MaxRequests: number | ''
  TimeWindow: string
}

/** One rule: the role may call the action on the item. */
export interface SecurityRule {
  Type: string
  Name: string
  RoleID: string
  Action: string
  Record: string
  Properties: string[]
  RateLimit: RateLimit | null
}

/** What `?item=` holds: 'Entity-Customers', 'CustomEndpoint-Report', 'Schema-Schema'. */
export function itemKey(item: { Type: string; Name: string }): string {
  return `${item.Type}-${item.Name}`
}

/** The actions of an item, in column order. Every item can be read. */
export function itemActions(item: SecurityItem): Action[] {
  const actions: Action[] = ['get']

  if (item.AllowPost) {
    actions.push('post')
  }

  if (item.AllowPut) {
    actions.push('put')
  }

  if (item.AllowDelete) {
    actions.push('delete')
  }

  return actions
}

/** The properties a rule of this action can list. Empty when the action has none to choose. */
export function offeredProperties(item: SecurityItem, action: string): string[] {
  switch (action.toLowerCase()) {
    case 'get':
      return item.PropertiesGet
    case 'post':
    case 'put':
      return item.PropertiesPostPut
    default:
      return []
  }
}

/**
 * Whether 'Owned records only' does anything for this cell: the entity needs an Owner column, the
 * caller has to be a signed-in user (an anonymous call has no owner to compare with), and a post
 * makes a new record rather than reading existing ones.
 */
export function ownedApplies(item: SecurityItem, roleId: string, action: string): boolean {
  return item.HasOwner && roleId !== ANONYMOUS && action.toLowerCase() !== 'post'
}

/** Whether the rule is the one of this cell (the item, the role and the action). */
export function isCell(rule: SecurityRule, item: SecurityItem, roleId: string, action: string): boolean {
  return (
    rule.Type === item.Type &&
    rule.Name === item.Name &&
    rule.RoleID === roleId &&
    rule.Action.toLowerCase() === action.toLowerCase()
  )
}

export function findRule(rules: readonly SecurityRule[], item: SecurityItem, roleId: string, action: string): SecurityRule | undefined {
  return rules.find((rule) => isCell(rule, item, roleId, action))
}

/** The roles whose rules apply to a caller of this role: Anonymous to everyone, Authenticated to every signed-in user. */
export function rolesThatApply(roleId: string): string[] {
  if (roleId === ANONYMOUS) {
    return [ANONYMOUS]
  }

  if (roleId === AUTHENTICATED) {
    return [ANONYMOUS, AUTHENTICATED]
  }

  return [ANONYMOUS, AUTHENTICATED, roleId]
}

/**
 * The note next to a switch of the editor: when Anonymous has the
 * cell, every other role inherits it (even with its own rule on); otherwise a role without its own
 * rule inherits what Authenticated has.
 */
export function inheritance(
  rules: readonly SecurityRule[],
  item: SecurityItem,
  roleId: string,
  action: string,
): 'anonymous' | 'authenticated' | undefined {
  if (roleId === ANONYMOUS) {
    return undefined
  }

  if (findRule(rules, item, ANONYMOUS, action)) {
    return 'anonymous'
  }

  if (roleId !== AUTHENTICATED && !findRule(rules, item, roleId, action) && findRule(rules, item, AUTHENTICATED, action)) {
    return 'authenticated'
  }

  return undefined
}

export interface PropertyAccess {
  /**
   * all: every property the action offers. some: part of them. none: an empty list, which gives
   * only the ID on a get and refuses a post or put. fixed: the action has no properties to choose
   * (delete, the schema, a custom endpoint, a file upload).
   */
  kind: 'all' | 'some' | 'none' | 'fixed'
  included: string[]
  excluded: string[]
}

/** How the listed properties compare with what the action offers. The API server compares names ignoring case. */
export function propertyAccess(item: SecurityItem, action: string, listed: readonly string[]): PropertyAccess {
  const offered = offeredProperties(item, action)

  if (offered.length === 0) {
    return { kind: 'fixed', included: [], excluded: [] }
  }

  const names = new Set(listed.map((name) => name.toLowerCase()))
  const included = offered.filter((name) => names.has(name.toLowerCase()))
  const excluded = offered.filter((name) => !names.has(name.toLowerCase()))
  const kind = included.length === 0 ? 'none' : excluded.length === 0 ? 'all' : 'some'

  return { kind, included, excluded }
}

const windowSeconds: Record<string, number> = { Per_Second: 1, Per_Minute: 60, Per_Hour: 3600 }
const windowShort: Record<string, string> = { Per_Second: 'sec', Per_Minute: 'min', Per_Hour: 'hr' }
const windowLong: Record<string, string> = { Per_Second: 'second', Per_Minute: 'minute', Per_Hour: 'hour' }

/** '10/min', as the tree and the matrix show it. */
export function rateLimitShort(rateLimit: RateLimit): string {
  return `${rateLimit.MaxRequests}/${windowShort[rateLimit.TimeWindow] ?? rateLimit.TimeWindow}`
}

/** '10 requests per minute'. */
export function rateLimitText(rateLimit: RateLimit): string {
  const noun = rateLimit.MaxRequests === 1 ? 'request' : 'requests'
  return `${rateLimit.MaxRequests} ${noun} per ${windowLong[rateLimit.TimeWindow] ?? rateLimit.TimeWindow}`
}

/**
 * The rate limit the API server applies to an item and action. It looks at every rule of that name
 * and action, whatever the role (and whatever the type: an entity and a custom endpoint of the same
 * name share it): when one of them has no limit there is none, otherwise the most generous one
 * applies, counted per signed-in user, with every call without a valid auth token sharing one count.
 */
export function endpointRateLimit(rules: readonly SecurityRule[], item: SecurityItem, action: string): RateLimit | null {
  const matching = rules.filter(
    (rule) => rule.Name.toLowerCase() === item.Name.toLowerCase() && rule.Action.toLowerCase() === action.toLowerCase(),
  )

  if (matching.length === 0 || matching.some((rule) => rule.RateLimit === null)) {
    return null
  }

  const perSecond = (limit: RateLimit) => Number(limit.MaxRequests) / (windowSeconds[limit.TimeWindow] ?? 1)

  return matching
    .map((rule) => rule.RateLimit)
    .filter((limit): limit is RateLimit => limit !== null)
    .reduce((widest, limit) => (perSecond(limit) > perSecond(widest) ? limit : widest))
}

/** What a caller of one role may do with one action of an item. */
export interface Access {
  action: Action
  allowed: boolean
  /** The roles whose rules give the access, Anonymous and Authenticated first. */
  from: string[]
  /** Allowed only through Anonymous or Authenticated, with no rule of the role itself. */
  inherited: boolean
  /** Only the records the user owns. */
  owned: boolean
  properties: PropertyAccess
  /** The limit of the item and action as a whole (see endpointRateLimit). */
  rateLimit: RateLimit | null
  /** Allowed on every record with every property the action offers. */
  full: boolean
}

/**
 * The access of a role, as the API server works it out for a caller who has that role: the rules
 * of Anonymous, Authenticated and the role together. Properties add up; 'owned records only' in
 * any of them wins.
 */
export function effectiveAccess(rules: readonly SecurityRule[], item: SecurityItem, roleId: string, action: Action): Access {
  const applying = rolesThatApply(roleId)
    .map((id) => findRule(rules, item, id, action))
    .filter((rule): rule is SecurityRule => rule !== undefined)

  const allowed = applying.length > 0
  const owned = allowed && ownedApplies(item, roleId, action) && applying.some((rule) => rule.Record === 'Owned')
  const properties = propertyAccess(
    item,
    action,
    applying.flatMap((rule) => rule.Properties),
  )

  return {
    action,
    allowed,
    from: applying.map((rule) => rule.RoleID),
    inherited: allowed && !applying.some((rule) => rule.RoleID === roleId),
    owned,
    properties,
    rateLimit: allowed ? endpointRateLimit(rules, item, action) : null,
    full: allowed && !owned && (properties.kind === 'all' || properties.kind === 'fixed'),
  }
}

/** The access of a role to every action of an item, in column order. */
export function itemAccess(rules: readonly SecurityRule[], item: SecurityItem, roleId: string): Access[] {
  return itemActions(item).map((action) => effectiveAccess(rules, item, roleId, action))
}

/** 'No properties' in words: what an empty list does for this action. */
export function noPropertiesText(action: string): string {
  return action.toLowerCase() === 'get' ? 'No properties: only the ID is returned' : 'No properties: the call is refused'
}

/**
 * The access in short phrases, for the tree and the matrix: what is restricted, then the rate
 * limit. 'Full access' when nothing is.
 */
export function describeAccess(access: Access): string[] {
  if (!access.allowed) {
    return ['No access']
  }

  const parts: string[] = []

  if (access.owned) {
    parts.push('Owned records only')
  }

  if (access.properties.kind === 'none') {
    parts.push(noPropertiesText(access.action))
  } else if (access.properties.kind === 'some') {
    parts.push(`Included: ${access.properties.included.join(', ')}`)
    parts.push(`Excluded: ${access.properties.excluded.join(', ')}`)
  }

  if (parts.length === 0) {
    parts.push('Full access')
  }

  if (access.rateLimit) {
    parts.push(`Rate limit ${rateLimitShort(access.rateLimit)}`)
  }

  return parts
}

/** 'Anonymous users' for ANONYMOUS, from the role list; the role id when it is not listed. */
export function roleName(roles: readonly SecurityRole[], roleId: string): string {
  return roles.find((role) => role.RoleID === roleId)?.DisplayName ?? roleId
}
