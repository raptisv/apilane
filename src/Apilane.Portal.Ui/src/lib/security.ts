import type { Schemas } from './api'
import { isCell, itemKey } from './securityAccess'
import type { SecurityItem, SecurityRule } from './securityAccess'

/**
 * The editing rules of the security screen: the rules as the editor holds them, the body of the
 * save and what changed. What a rule grants is in
 * securityAccess.ts. Pure functions.
 */

/** A rule from the API, as the editor holds it: copies of its lists, and null for no rate limit. */
export function toEditable(rule: Schemas['SecurityRuleResponse']): SecurityRule {
  return {
    Type: rule.Type,
    Name: rule.Name,
    RoleID: rule.RoleID,
    Action: rule.Action,
    Record: rule.Record,
    Properties: [...rule.Properties],
    RateLimit: rule.RateLimit ? { MaxRequests: rule.RateLimit.MaxRequests, TimeWindow: rule.RateLimit.TimeWindow } : null,
  }
}

/**
 * A cell switched on: every record, no rate limit and no properties.
 * The editor shows that as 'no properties' and offers 'Select all'.
 */
export function newRule(item: SecurityItem, roleId: string, action: string): SecurityRule {
  return { Type: item.Type, Name: item.Name, RoleID: roleId, Action: action, Record: 'All', Properties: [], RateLimit: null }
}

/**
 * A cell switched on again before a save: the properties and the rate limit it had when it was
 * switched off come back (a slip of the hand loses nothing). The record scope starts at 'All'
 * again. Without a remembered rule the new one is returned as it is.
 */
export function restoreRule(fresh: SecurityRule, kept: SecurityRule | undefined): SecurityRule {
  return kept ? { ...fresh, Properties: [...kept.Properties], RateLimit: kept.RateLimit ? { ...kept.RateLimit } : null } : fresh
}

/**
 * The rules with the one of this cell replaced, added (at the end) or, with `next` undefined,
 * removed.
 */
export function setRule(
  rules: readonly SecurityRule[],
  item: SecurityItem,
  roleId: string,
  action: string,
  next: SecurityRule | undefined,
): SecurityRule[] {
  const index = rules.findIndex((rule) => isCell(rule, item, roleId, action))

  if (index === -1) {
    return next ? [...rules, next] : [...rules]
  }

  return next ? rules.map((rule, i) => (i === index ? next : rule)) : rules.filter((_, i) => i !== index)
}

/** The body of the save. An empty number box is sent as 0, which the API rejects at that rule. */
export function rulesRequest(rules: readonly SecurityRule[]): Schemas['SecurityRulesRequest'] {
  return {
    Rules: rules.map((rule) => ({
      Type: rule.Type,
      Name: rule.Name,
      RoleID: rule.RoleID,
      Action: rule.Action,
      Record: rule.Record,
      Properties: rule.Properties,
      RateLimit: rule.RateLimit
        ? { MaxRequests: wholeNumber(rule.RateLimit.MaxRequests), TimeWindow: rule.RateLimit.TimeWindow }
        : undefined,
    })),
  }
}

function wholeNumber(value: number | ''): number {
  return typeof value === 'number' && Number.isFinite(value) ? Math.trunc(value) : 0
}

/** The rule in one string, ignoring the order of its properties: two rules with the same text are the same. */
function ruleText(rule: SecurityRule): string {
  const rate = rule.RateLimit ? `${String(rule.RateLimit.MaxRequests)}/${rule.RateLimit.TimeWindow}` : ''
  return [rule.Type, rule.Name, rule.RoleID, rule.Action.toLowerCase(), rule.Record, [...rule.Properties].sort().join(','), rate].join('|')
}

/**
 * The keys (itemKey) of the items whose rules differ between the saved and the edited list. The
 * order of the rules does not count: switching a cell off and on again is no change.
 */
export function changedItems(saved: readonly SecurityRule[], edited: readonly SecurityRule[]): Set<string> {
  const savedTexts = new Set(saved.map(ruleText))
  const editedTexts = new Set(edited.map(ruleText))
  const changed = new Set<string>()

  for (const rule of edited) {
    if (!savedTexts.has(ruleText(rule))) {
      changed.add(itemKey(rule))
    }
  }

  for (const rule of saved) {
    if (!editedTexts.has(ruleText(rule))) {
      changed.add(itemKey(rule))
    }
  }

  return changed
}

/** What the bar at the bottom says while rules are not saved. */
export function changesText(changedItemCount: number): string {
  return changedItemCount === 1 ? 'Unsaved rule changes in 1 item.' : `Unsaved rule changes in ${changedItemCount} items.`
}
