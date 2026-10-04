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

/** The entities in the order of the data browser: the custom ones first, then the system ones, each group in the given order. */
export function dataEntityOrder(entities: readonly Entity[]): Entity[] {
  return [...entities.filter((entity) => !entity.IsSystem), ...entities.filter((entity) => entity.IsSystem)]
}

/**
 * The entity the data browser opens when the address names none: the one `asked` names (the
 * ?entity= of the classic data browser; names are case-sensitive), else the first of the list.
 */
export function initialDataEntity(entities: readonly Entity[], asked: string | undefined): Entity | undefined {
  return entities.find((entity) => entity.Name === asked) ?? entities[0]
}

/** A count as a person reads it: 1234567 -> '1,234,567'. */
export function formatCount(count: number): string {
  return count.toLocaleString('en-US')
}
