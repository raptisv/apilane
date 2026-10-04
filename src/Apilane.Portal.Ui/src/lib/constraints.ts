import type { Schemas } from './api'
import type { Constraint } from './entities'

/**
 * The editing rules of the constraints screen: what is saved, what was added, what will be
 * removed, and what goes into the request. Pure functions, no Vue (constraints.test.ts).
 * The texts of a constraint (label, on-delete words) are in entities.ts.
 */
export type ConstraintRequest = Schemas['ConstraintRequest']

/** One line of the constraints list. */
export interface ConstraintRow {
  constraint: Constraint
  /** 'saved': stored and kept. 'added': not saved yet. 'removed': stored, gone after the next save. */
  status: 'saved' | 'added' | 'removed'
  /** The place of the constraint in the request of the next save, where the API reports its problems. */
  requestIndex: number | undefined
}

const noAction = 'ON_DELETE_NO_ACTION'

/** What each on-delete action does, for the foreign key form. */
export const onDeleteHelp: Record<string, string> = {
  ON_DELETE_NO_ACTION: 'A record cannot be deleted while other records point to it.',
  ON_DELETE_SET_NULL: 'When a record is deleted, the property of the records that point to it is set to null.',
  ON_DELETE_CASCADE: 'When a record is deleted, the records that point to it are deleted too.',
}

/**
 * What makes two constraints the same one for the API, which refuses duplicates: the same set of
 * unique properties in any order, or the same foreign key property and entity whatever the action.
 */
export function constraintKey(constraint: Constraint): string {
  return constraint.Type === 'ForeignKey'
    ? `ForeignKey:${constraint.Property},${constraint.ForeignEntity}`
    : `${constraint.Type}:${[...constraint.Properties].sort().join(',')}`
}

/** True when nothing differs: the same key and, for a foreign key, the same on-delete action. */
export function sameConstraint(a: Constraint, b: Constraint): boolean {
  return constraintKey(a) === constraintKey(b) && (a.OnDelete || noAction) === (b.OnDelete || noAction)
}

/**
 * The list the screen shows: the saved constraints in their order, each marked when it is about
 * to be removed, then the added ones.
 */
export function constraintRows(
  saved: readonly Constraint[],
  removedSaved: readonly Constraint[],
  added: readonly Constraint[],
): ConstraintRow[] {
  const rows: ConstraintRow[] = []
  let next = 0

  for (const constraint of saved) {
    const removed = !constraint.IsSystem && removedSaved.includes(constraint)

    rows.push({
      constraint,
      status: removed ? 'removed' : 'saved',
      // System constraints are kept by the server and are not sent.
      requestIndex: removed || constraint.IsSystem ? undefined : next++,
    })
  }

  for (const constraint of added) {
    rows.push({ constraint, status: 'added', requestIndex: next++ })
  }

  return rows
}

/** The body of the save: every custom constraint the entity should have afterwards. */
export function constraintRequests(rows: readonly ConstraintRow[]): ConstraintRequest[] {
  return rows
    .filter((row) => row.requestIndex !== undefined)
    .map(({ constraint }) =>
      constraint.Type === 'ForeignKey'
        ? {
            Type: constraint.Type,
            Property: constraint.Property,
            ForeignEntity: constraint.ForeignEntity,
            OnDelete: constraint.OnDelete || noAction,
          }
        : { Type: constraint.Type, Properties: constraint.Properties },
    )
}

/** The pending edits of the list: what Save would add and remove. */
export interface ConstraintEdits {
  /**
   * The saved constraints that are to be removed, as the very objects of the saved list: rows the
   * Razor page stored twice are removed one at a time.
   */
  removed: Constraint[]
  /** The constraints that are not saved yet. */
  added: Constraint[]
}

/**
 * The edits after adding `constraint`. Adding back a saved constraint that is about to be removed,
 * unchanged, cancels that removal instead of removing and adding the same thing.
 */
export function addConstraint(saved: readonly Constraint[], edits: ConstraintEdits, constraint: Constraint): ConstraintEdits {
  const restored = saved.find((item) => edits.removed.includes(item) && sameConstraint(item, constraint))

  return restored
    ? { removed: edits.removed.filter((item) => item !== restored), added: [...edits.added] }
    : { removed: [...edits.removed], added: [...edits.added, constraint] }
}

/**
 * The edits after undoing the removal of `constraint`. An added constraint with the same key took
 * its place, so it goes: keeping both would be a duplicate.
 */
export function undoRemoval(edits: ConstraintEdits, constraint: Constraint): ConstraintEdits {
  const key = constraintKey(constraint)

  return {
    removed: edits.removed.filter((item) => item !== constraint),
    added: edits.added.filter((item) => constraintKey(item) !== key),
  }
}

/** What Save would do, for the unsaved-changes bar: '1 constraint to add, 2 to remove'. */
export function changesText(edits: ConstraintEdits): string {
  const added = edits.added.length
  const removed = edits.removed.length
  const noun = (count: number) => (count === 1 ? 'constraint' : 'constraints')

  if (added > 0 && removed > 0) {
    return `${added} ${noun(added)} to add, ${removed} to remove.`
  }

  return added > 0 ? `${added} ${noun(added)} to add.` : `${removed} ${noun(removed)} to remove.`
}

/** True when the list already has this constraint: a kept or added one with the same key. */
export function isDuplicate(rows: readonly ConstraintRow[], constraint: Constraint): boolean {
  const key = constraintKey(constraint)

  return rows.some((row) => row.status !== 'removed' && constraintKey(row.constraint) === key)
}

/**
 * True when `constraint` is a foreign key that is about to be removed, added back with another
 * on-delete action. The API refuses that in one save: the API server would leave the table as it is.
 */
export function changesOnDelete(rows: readonly ConstraintRow[], constraint: Constraint): boolean {
  const key = constraintKey(constraint)
  const removed = rows.filter((row) => row.status === 'removed' && constraintKey(row.constraint) === key)

  return constraint.Type === 'ForeignKey' && removed.length > 0 && !removed.some((row) => sameConstraint(row.constraint, constraint))
}
