import {
  ANONYMOUS,
  AUTHENTICATED,
  itemActions,
  offeredProperties,
  ownedApplies,
  propertyAccess,
  rateLimitShort,
  roleName,
} from './securityAccess'
import type { Action, SecurityItem, SecurityRole, SecurityRule } from './securityAccess'

/**
 * A short answer to "who may call this?", for the entity list, the custom endpoint list and the
 * properties of an entity: one of five kinds (an icon and a colour), and the lines of a tooltip. It reads
 * the rules as they are stored, the way the security screen lists them, and the properties through
 * securityAccess, so it never disagrees with that screen. Pure functions.
 */

export type AccessKind = 'publicWrite' | 'public' | 'signedIn' | 'roles' | 'closed'

/** A role that may call an action, with what restricts it: '5 of 7 properties', 'own records', '100/min'. */
export interface AccessWho {
  name: string
  notes: string[]
}

export interface AccessLine {
  action: Action
  /** Empty: nobody may call it. */
  who: AccessWho[]
}

export interface AccessSummary {
  kind: AccessKind
  /** The kind in words: 'Public', 'Signed-in users'. */
  label: string
  /** What the kind means, in a sentence. */
  hint: string
  lines: AccessLine[]
}

const kindTexts: Record<AccessKind, { label: string; hint: string }> = {
  publicWrite: { label: 'Public, can write', hint: 'Anyone on the internet can change data here, without signing in.' },
  public: { label: 'Public', hint: 'Anyone can read this without signing in.' },
  signedIn: { label: 'Signed-in users', hint: 'Any signed-in user can call it. Nobody else.' },
  roles: { label: 'Some roles', hint: 'Only users who have one of the listed roles can call it.' },
  closed: { label: 'Closed', hint: 'No rule gives access to it, so every call is refused.' },
}

const sameName = (left: string, right: string) => left.toLowerCase() === right.toLowerCase()

/** Anonymous first, then Authenticated, then the other roles by name. */
function byRole(roles: readonly SecurityRole[]) {
  const rank = (roleId: string) => (roleId === ANONYMOUS ? 0 : roleId === AUTHENTICATED ? 1 : 2)

  return (left: SecurityRule, right: SecurityRule) =>
    rank(left.RoleID) - rank(right.RoleID) || roleName(roles, left.RoleID).localeCompare(roleName(roles, right.RoleID))
}

/** The strongest exposure among the rules: public over signed-in over roles, and public with a write is its own kind. */
function kindOf(rules: readonly { RoleID: string; Action: string }[]): AccessKind {
  const anonymous = rules.filter((rule) => rule.RoleID === ANONYMOUS)

  if (anonymous.length > 0) {
    return anonymous.some((rule) => rule.Action.toLowerCase() !== 'get') ? 'publicWrite' : 'public'
  }

  if (rules.some((rule) => rule.RoleID === AUTHENTICATED)) {
    return 'signedIn'
  }

  return rules.length > 0 ? 'roles' : 'closed'
}

function summary(kind: AccessKind, lines: AccessLine[], hint?: string): AccessSummary {
  return { kind, label: kindTexts[kind].label, hint: hint ?? kindTexts[kind].hint, lines }
}

/** What restricts a rule, in short phrases. */
function ruleNotes(item: SecurityItem, rule: SecurityRule, action: Action): string[] {
  const notes: string[] = []
  const properties = propertyAccess(item, action, rule.Properties)

  if (properties.kind === 'some') {
    notes.push(`${properties.included.length} of ${properties.included.length + properties.excluded.length} properties`)
  } else if (properties.kind === 'none') {
    notes.push(action === 'get' ? 'only the ID' : 'refused: no properties')
  }

  if (rule.Record === 'Owned' && ownedApplies(item, rule.RoleID, action)) {
    notes.push('own records')
  }

  if (rule.RateLimit) {
    notes.push(rateLimitShort(rule.RateLimit))
  }

  return notes
}

const rulesOf = (item: SecurityItem, rules: readonly SecurityRule[]) =>
  rules.filter((rule) => rule.Type === item.Type && rule.Name === item.Name)

/** Who may call an entity, a custom endpoint or the schema, for each action it offers. */
export function itemSummary(item: SecurityItem, rules: readonly SecurityRule[], roles: readonly SecurityRole[]): AccessSummary {
  const own = rulesOf(item, rules)

  const lines = itemActions(item).map((action) => ({
    action,
    who: own
      .filter((rule) => rule.Action.toLowerCase() === action)
      .sort(byRole(roles))
      .map((rule) => ({ name: roleName(roles, rule.RoleID), notes: ruleNotes(item, rule, action) })),
  }))

  return summary(kindOf(own), lines)
}

/**
 * Who may read or write one property of an entity: the roles whose rules list it (a get that lists no property
 * still returns the ID). Only the actions that offer the property get a line; none at all means the API never
 * exposes it.
 */
export function propertySummary(
  item: SecurityItem,
  propertyName: string,
  rules: readonly SecurityRule[],
  roles: readonly SecurityRole[],
): AccessSummary {
  const own = rulesOf(item, rules)
  const actions = (['get', 'post', 'put'] as const).filter(
    (action) =>
      itemActions(item).includes(action) &&
      // A get always returns the ID, whatever the rule lists.
      ((action === 'get' && sameName(propertyName, 'ID')) || offeredProperties(item, action).some((name) => sameName(name, propertyName))),
  )

  if (actions.length === 0) {
    return summary('closed', [], 'The API never returns or accepts this property.')
  }

  const lists = (rule: SecurityRule, action: Action) =>
    rule.Action.toLowerCase() === action &&
    (rule.Properties.some((name) => sameName(name, propertyName)) || (action === 'get' && sameName(propertyName, 'ID')))
  const giving = own.filter((rule) => actions.some((action) => lists(rule, action)))

  const lines = actions.map((action) => ({
    action,
    who: giving
      .filter((rule) => lists(rule, action))
      .sort(byRole(roles))
      .map((rule) => ({
        name: roleName(roles, rule.RoleID),
        notes: rule.Record === 'Owned' && ownedApplies(item, rule.RoleID, action) ? ['own records'] : [],
      })),
  }))

  return summary(kindOf(giving), lines, giving.length === 0 ? 'No rule lists this property, so it is never returned or accepted.' : undefined)
}
