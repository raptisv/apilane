import type { Schemas } from './api'

/**
 * How the comparison of two applications is laid out for the compare dialog: the answer of
 * GET /applications/{appToken}/comparison nests the properties and constraints inside each changed
 * entity; the dialog lists them in sections of their own. Pure functions, no Vue, so they can be
 * unit-tested (comparison.test.ts).
 */
export type Comparison = Schemas['ApplicationComparisonResponse']
export type ComparedEntity = Schemas['ComparisonEntity']
export type ComparedProperty = Schemas['ComparisonProperty']
/** A property as a list shows it. One removed from an entity both applications have comes with its name and type only. */
export type ListedProperty = ComparedProperty | Schemas['ComparisonRemovedProperty']
export type ComparedConstraint = Schemas['ComparisonConstraint']
export type ComparedEndpoint = Schemas['ComparisonCustomEndpoint']
export type ComparedRule = Schemas['ComparisonSecurityRule']

/** One value that differs: what FieldChange draws. `code` is for SQL, shown as a block. */
export interface ValueChange {
  field: string
  before: string | null | undefined
  after: string | null | undefined
  code?: boolean
}

/** Something that has a name and the values that differ in it. */
export interface NamedChanges {
  name: string
  changes: ValueChange[]
}

/** A property or constraint of an entity both applications have. */
export interface OfEntity<T> {
  entity: string
  item: T
}

export interface ComparisonView {
  entities: { added: ComparedEntity[]; removed: ComparedEntity[]; changed: NamedChanges[] }
  properties: { added: OfEntity<ListedProperty>[]; removed: OfEntity<ListedProperty>[]; changed: NamedChanges[] }
  constraints: { added: OfEntity<ComparedConstraint>[]; removed: OfEntity<ComparedConstraint>[] }
  customEndpoints: { added: ComparedEndpoint[]; removed: ComparedEndpoint[]; changed: NamedChanges[] }
  security: { added: ComparedRule[]; removed: ComparedRule[]; changed: NamedChanges[] }
}

/**
 * The five sections of the dialog. An entity both applications have shows under Entities only when
 * its own values (description, flags) differ; its properties and constraints go to their sections,
 * named 'Entity.Property'.
 */
export function comparisonView(comparison: Comparison): ComparisonView {
  const changed = comparison.Entities.Changed

  function ofEntity<T>(pick: (entity: Schemas['ComparisonChangedEntity']) => T[]): OfEntity<T>[] {
    return changed.flatMap((entity) => pick(entity).map((item) => ({ entity: entity.Name, item })))
  }

  return {
    entities: {
      added: comparison.Entities.Added,
      removed: comparison.Entities.Removed,
      changed: changed
        .filter((entity) => entity.MetadataChanges.length > 0)
        .map((entity) => ({ name: entity.Name, changes: entity.MetadataChanges.map(valueChange) })),
    },
    properties: {
      added: ofEntity((entity) => entity.PropertiesAdded),
      removed: ofEntity((entity) => entity.PropertiesRemoved),
      changed: changed.flatMap((entity) =>
        entity.PropertiesChanged.map((property) => ({
          name: `${entity.Name}.${property.Name}`,
          changes: property.Changes.map(valueChange),
        })),
      ),
    },
    constraints: {
      added: ofEntity((entity) => entity.ConstraintsAdded),
      removed: ofEntity((entity) => entity.ConstraintsRemoved),
    },
    customEndpoints: {
      added: comparison.CustomEndpoints.Added,
      removed: comparison.CustomEndpoints.Removed,
      changed: comparison.CustomEndpoints.Changed.map((endpoint) => ({ name: endpoint.Name, changes: endpointChanges(endpoint) })),
    },
    security: {
      added: comparison.Security.Added,
      removed: comparison.Security.Removed,
      changed: comparison.Security.Changed.map((rule) => ({ name: rule.Name, changes: ruleChanges(rule) })),
    },
  }
}

/** Whether there is nothing to show: 'Applications are identical'. */
export function isIdentical(view: ComparisonView): boolean {
  const sections: Record<string, readonly unknown[]>[] = Object.values(view)

  return sections.every((section) => Object.values(section).every((list) => list.length === 0))
}

function valueChange(change: Schemas['ComparisonFieldChange']): ValueChange {
  return { field: change.Field, before: change.Before, after: change.After }
}

/** The description and the query of a custom endpoint, each only when it differs. */
function endpointChanges(endpoint: Schemas['ComparisonChangedCustomEndpoint']): ValueChange[] {
  const changes: ValueChange[] = []

  if (endpoint.DescriptionBefore !== endpoint.DescriptionAfter) {
    changes.push({ field: 'Description', before: endpoint.DescriptionBefore, after: endpoint.DescriptionAfter })
  }

  if (endpoint.QueryBefore !== endpoint.QueryAfter) {
    changes.push({ field: 'Query', before: endpoint.QueryBefore, after: endpoint.QueryAfter, code: true })
  }

  return changes
}

/**
 * Of record scope, rate limit and properties of a security rule, the ones that differ. Role and
 * action are part of what identifies a rule, so they are the same on both sides.
 */
function ruleChanges(rule: Schemas['ComparisonChangedSecurityRule']): ValueChange[] {
  const before = rule.SecurityBefore
  const after = rule.SecurityAfter

  return [
    { field: 'Record', before: before.Record, after: after.Record },
    { field: 'Rate limit', before: before.RateLimit ?? 'none', after: after.RateLimit ?? 'none' },
    { field: 'Properties', before: before.Properties, after: after.Properties },
  ].filter((change) => change.before !== change.after)
}

/** A security rule in one line: 'ANONYMOUS · get · All', with ' · limit: 10 request per minute' when it has one. */
export function ruleText(rule: ComparedRule): string {
  const parts = [rule.Role, rule.Action, rule.Record].filter((part) => part !== '')

  if (rule.RateLimit) {
    parts.push(`limit: ${rule.RateLimit}`)
  }

  return parts.join(' · ')
}

/** The stored values of a property that are set, in short words: 'Required', 'Min 0', 'Max 200', '2 decimals'. */
export function propertyFacts(listed: ListedProperty): string[] {
  const property: Partial<ComparedProperty> = listed
  const facts: string[] = []

  if (property.Required) {
    facts.push('Required')
  }

  if (property.Minimum != null) {
    facts.push(`Min ${property.Minimum}`)
  }

  if (property.Maximum != null) {
    facts.push(`Max ${property.Maximum}`)
  }

  if (property.DecimalPlaces != null) {
    facts.push(property.DecimalPlaces === 1 ? '1 decimal' : `${property.DecimalPlaces} decimals`)
  }

  if (property.Encrypted) {
    facts.push('Encrypted')
  }

  if (property.ValidationRegex) {
    facts.push(`Regex ${property.ValidationRegex}`)
  }

  return facts
}

/** The kind of a constraint by its stored number: 1 'Unique', 2 'Foreign key'. */
export function constraintTypeName(typeId: number): string {
  return typeId === 1 ? 'Unique' : typeId === 2 ? 'Foreign key' : `Type ${typeId}`
}
