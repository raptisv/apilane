import type { Schemas } from './api'

/**
 * How the entities of an application are arranged and described, for the entities list and the
 * entity screens. Pure functions, no Vue, so they can be unit-tested (entities.test.ts).
 */
export type Entity = Schemas['EntityResponse']
export type Constraint = Schemas['ConstraintResponse']

/** The entities whose name contains `search`, ignoring case. An empty search keeps them all. */
export function filterEntities(entities: readonly Entity[], search: string): Entity[] {
  const text = search.trim().toLowerCase()

  return entities.filter((entity) => entity.Name.toLowerCase().includes(text))
}

const onDeleteTexts: Record<string, string> = {
  ON_DELETE_NO_ACTION: 'no action',
  ON_DELETE_SET_NULL: 'set null',
  ON_DELETE_CASCADE: 'cascade',
}

/** What a foreign key does when the record it points to is deleted, in plain words: 'cascade'. */
export function onDeleteText(onDelete: string | null | undefined): string {
  // A foreign key stored without the rule does nothing on delete; an unknown value is shown as it is.
  return onDelete ? (onDeleteTexts[onDelete] ?? onDelete) : onDeleteTexts.ON_DELETE_NO_ACTION
}

/** The kind of a constraint in plain words: 'Unique' or 'Foreign key'. */
export function constraintTypeText(type: string): string {
  return type === 'ForeignKey' ? 'Foreign key' : type
}

/** The short text of a constraint, for a badge: 'Email, Phone' or 'CustomerID → Customers'. */
export function constraintLabel(constraint: Constraint): string {
  return constraint.Type === 'ForeignKey'
    ? `${constraint.Property} → ${constraint.ForeignEntity}`
    : constraint.Properties.join(', ')
}

/**
 * The full text of a constraint, for a tooltip: 'Unique: Email, Phone' or
 * 'Foreign key: CustomerID → Customers, on delete cascade'.
 */
export function constraintDescription(constraint: Constraint): string {
  return constraint.Type === 'ForeignKey'
    ? `Foreign key: ${constraintLabel(constraint)}, on delete ${onDeleteText(constraint.OnDelete)}`
    : `Unique: ${constraintLabel(constraint)}`
}

/** A constraint a property is part of, as one small badge on the properties screen. */
export interface PropertyConstraint {
  type: 'Unique' | 'ForeignKey'
  /** The short text: 'Unique', 'Unique with Phone' or '→ Customers'. */
  label: string
  /** The full text of the constraint, for a tooltip and a screen reader. */
  description: string
  /** True for a constraint the entity was created with. */
  isSystem: boolean
}

/**
 * The constraints of the entity that a property is part of: a unique constraint that lists it (alone, or
 * together with others) and a foreign key on it. The names are compared as the API stores them, without regard
 * to case, as the database does.
 */
export function propertyConstraints(propertyName: string, constraints: readonly Constraint[]): PropertyConstraint[] {
  const same = (name: string | null | undefined) => (name ?? '').toLowerCase() === propertyName.toLowerCase()
  const found: PropertyConstraint[] = []

  for (const constraint of constraints) {
    if (constraint.Type === 'ForeignKey') {
      if (same(constraint.Property)) {
        const onDelete = onDeleteText(constraint.OnDelete)
        found.push({
          type: 'ForeignKey',
          label: `→ ${constraint.ForeignEntity}${onDelete === onDeleteTexts.ON_DELETE_NO_ACTION ? '' : ` (${onDelete})`}`,
          description: constraintDescription(constraint),
          isSystem: constraint.IsSystem,
        })
      }
    } else if (constraint.Properties.some(same)) {
      const others = constraint.Properties.filter((name) => !same(name))
      found.push({
        type: 'Unique',
        label: others.length === 0 ? 'Unique' : `Unique with ${others.join(', ')}`,
        description: constraintDescription(constraint),
        isSystem: constraint.IsSystem,
      })
    }
  }

  return found
}

/**
 * The first entity with a foreign key that points to `entityName` (the entity itself included), or undefined.
 * While there is one the API refuses to rename or delete the entity. Names are compared as the API does
 * there: case-sensitive.
 */
export function referencingEntity(entityName: string, entities: readonly Entity[]): string | undefined {
  return entities.find((entity) =>
    entity.Constraints.some((constraint) => constraint.Type === 'ForeignKey' && constraint.ForeignEntity === entityName),
  )?.Name
}

/** The entities in the order of the data browser: the custom ones first, then the system ones, each group in the given order. */
export function dataEntityOrder(entities: readonly Entity[]): Entity[] {
  return [...entities.filter((entity) => !entity.IsSystem), ...entities.filter((entity) => entity.IsSystem)]
}

/** A count as a person reads it: 1234567 -> '1,234,567'. */
export function formatCount(count: number): string {
  return count.toLocaleString('en-US')
}
