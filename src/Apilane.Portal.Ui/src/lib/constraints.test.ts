import { describe, expect, it } from 'vitest'
import {
  addConstraint,
  changesOnDelete,
  changesText,
  constraintKey,
  constraintRequests,
  constraintRows,
  isDuplicate,
  sameConstraint,
  undoRemoval,
} from './constraints'
import type { Constraint } from './entities'

function unique(...properties: string[]): Constraint {
  return { Type: 'Unique', IsSystem: false, Properties: properties }
}

function foreignKey(property: string, entity: string, onDelete: string | null = 'ON_DELETE_CASCADE'): Constraint {
  return { Type: 'ForeignKey', IsSystem: false, Properties: [], Property: property, ForeignEntity: entity, OnDelete: onDelete }
}

const system: Constraint = { ...unique('Email'), IsSystem: true }

describe('constraintKey', () => {
  it('ignores the order of unique properties', () => {
    expect(constraintKey(unique('Name', 'Email'))).toBe(constraintKey(unique('Email', 'Name')))
  })

  it('ignores the on-delete action of a foreign key', () => {
    expect(constraintKey(foreignKey('CustomerID', 'Customers', 'ON_DELETE_CASCADE'))).toBe(
      constraintKey(foreignKey('CustomerID', 'Customers', 'ON_DELETE_SET_NULL')),
    )
  })

  it('tells a unique constraint from a foreign key and one target from another', () => {
    expect(constraintKey(unique('CustomerID'))).not.toBe(constraintKey(foreignKey('CustomerID', 'Customers')))
    expect(constraintKey(foreignKey('CustomerID', 'Customers'))).not.toBe(constraintKey(foreignKey('CustomerID', 'Orders')))
  })
})

describe('sameConstraint', () => {
  it('compares the on-delete action of a foreign key, reading a missing one as no action', () => {
    expect(sameConstraint(foreignKey('A', 'B', null), foreignKey('A', 'B', 'ON_DELETE_NO_ACTION'))).toBe(true)
    expect(sameConstraint(foreignKey('A', 'B', 'ON_DELETE_CASCADE'), foreignKey('A', 'B', 'ON_DELETE_SET_NULL'))).toBe(false)
  })

  it('reads the same unique properties in another order as the same constraint', () => {
    expect(sameConstraint(unique('Name', 'Email'), unique('Email', 'Name'))).toBe(true)
  })
})

describe('constraintRows', () => {
  const saved = [system, unique('Name'), foreignKey('CustomerID', 'Customers')]

  it('shows the saved constraints as saved when nothing was edited', () => {
    const rows = constraintRows(saved, [], [])

    expect(rows.map((row) => row.status)).toEqual(['saved', 'saved', 'saved'])
    // The system constraint is not sent, so it has no place in the request.
    expect(rows.map((row) => row.requestIndex)).toEqual([undefined, 0, 1])
  })

  it('marks a removed constraint, keeps it in place and numbers the rest without it', () => {
    const rows = constraintRows(saved, [saved[1]], [unique('Code')])

    expect(rows.map((row) => row.status)).toEqual(['saved', 'removed', 'saved', 'added'])
    expect(rows.map((row) => row.requestIndex)).toEqual([undefined, undefined, 0, 1])
  })

  it('never marks a system constraint as removed', () => {
    expect(constraintRows(saved, [system], [])[0].status).toBe('saved')
  })

  it('marks only the removed one of two saved constraints with the same key', () => {
    const twins = [unique('A', 'B'), unique('B', 'A')]

    expect(constraintRows(twins, [twins[1]], []).map((row) => row.status)).toEqual(['saved', 'removed'])
  })
})

describe('constraintRequests', () => {
  it('sends the kept and the added custom constraints in the order of the list', () => {
    const saved = [system, unique('Name'), foreignKey('CustomerID', 'Customers', null), unique('Code')]
    const rows = constraintRows(saved, [saved[3]], [unique('Email', 'Phone')])

    expect(constraintRequests(rows)).toEqual([
      { Type: 'Unique', Properties: ['Name'] },
      // A foreign key stored without the action is sent with the one it has: no action.
      { Type: 'ForeignKey', Property: 'CustomerID', ForeignEntity: 'Customers', OnDelete: 'ON_DELETE_NO_ACTION' },
      { Type: 'Unique', Properties: ['Email', 'Phone'] },
    ])
  })

  it('sends an empty list when every custom constraint is removed', () => {
    const saved = [system, unique('Name')]

    expect(constraintRequests(constraintRows(saved, [saved[1]], []))).toEqual([])
  })
})

describe('isDuplicate', () => {
  const rows = constraintRows([system, unique('Name', 'Code'), foreignKey('CustomerID', 'Customers')], [], [unique('Phone')])

  it('finds a saved, a system and an added constraint with the same key', () => {
    expect(isDuplicate(rows, unique('Code', 'Name'))).toBe(true)
    expect(isDuplicate(rows, unique('Email'))).toBe(true)
    expect(isDuplicate(rows, unique('Phone'))).toBe(true)
    expect(isDuplicate(rows, foreignKey('CustomerID', 'Customers', 'ON_DELETE_SET_NULL'))).toBe(true)
  })

  it('accepts a new one', () => {
    expect(isDuplicate(rows, unique('Name'))).toBe(false)
    expect(isDuplicate(rows, foreignKey('CustomerID', 'Orders'))).toBe(false)
  })

  it('does not count a constraint that is about to be removed', () => {
    const saved = [foreignKey('CustomerID', 'Customers')]
    const edited = constraintRows(saved, saved, [])

    expect(isDuplicate(edited, foreignKey('CustomerID', 'Customers', 'ON_DELETE_SET_NULL'))).toBe(false)
  })
})

describe('addConstraint', () => {
  const saved = [system, unique('Name'), foreignKey('CustomerID', 'Customers', 'ON_DELETE_CASCADE')]

  it('adds a new constraint to the unsaved ones', () => {
    const edits = addConstraint(saved, { removed: [], added: [unique('Code')] }, unique('Email', 'Phone'))

    expect(edits).toEqual({ removed: [], added: [unique('Code'), unique('Email', 'Phone')] })
  })

  it('cancels the removal when the same saved constraint is added back', () => {
    expect(addConstraint(saved, { removed: [saved[1]], added: [] }, unique('Name'))).toEqual({ removed: [], added: [] })
  })

  it('adds a removed foreign key back as a new one when its on-delete action changed', () => {
    const changed = foreignKey('CustomerID', 'Customers', 'ON_DELETE_SET_NULL')

    expect(addConstraint(saved, { removed: [saved[2]], added: [] }, changed)).toEqual({ removed: [saved[2]], added: [changed] })
  })

  it('restores only the saved twin that is the same', () => {
    const twins = [foreignKey('CustomerID', 'Customers', 'ON_DELETE_CASCADE'), foreignKey('CustomerID', 'Customers', 'ON_DELETE_NO_ACTION')]
    const edits = addConstraint(twins, { removed: [...twins], added: [] }, foreignKey('CustomerID', 'Customers', 'ON_DELETE_CASCADE'))

    expect(edits.removed).toEqual([twins[1]])
    expect(edits.removed[0]).toBe(twins[1])
    expect(edits.added).toEqual([])
  })

  it('leaves the edits it was given alone', () => {
    const edits = { removed: [saved[1]], added: [] }
    addConstraint(saved, edits, unique('Name'))
    addConstraint(saved, edits, unique('Code'))

    expect(edits).toEqual({ removed: [saved[1]], added: [] })
  })
})

describe('changesText', () => {
  it('says what Save would do', () => {
    expect(changesText({ removed: [], added: [unique('A')] })).toBe('1 constraint to add.')
    expect(changesText({ removed: [unique('a'), unique('b')], added: [] })).toBe('2 constraints to remove.')
    expect(changesText({ removed: [unique('a')], added: [unique('A'), unique('B')] })).toBe('2 constraints to add, 1 to remove.')
  })
})

describe('undoRemoval', () => {
  it('restores the saved constraint and drops the added one that took its place', () => {
    const saved = foreignKey('CustomerID', 'Customers', 'ON_DELETE_CASCADE')
    const edits = { removed: [saved], added: [foreignKey('CustomerID', 'Customers', 'ON_DELETE_NO_ACTION'), unique('Code')] }

    expect(undoRemoval(edits, saved)).toEqual({ removed: [], added: [unique('Code')] })
  })
})

describe('changesOnDelete', () => {
  const saved = [foreignKey('CustomerID', 'Customers', 'ON_DELETE_CASCADE'), unique('Code')]

  it('finds a removed foreign key added back with another on-delete action', () => {
    const rows = constraintRows(saved, [saved[0]], [])

    expect(changesOnDelete(rows, foreignKey('CustomerID', 'Customers', 'ON_DELETE_SET_NULL'))).toBe(true)
    expect(changesOnDelete(rows, foreignKey('CustomerID', 'Customers', 'ON_DELETE_CASCADE'))).toBe(false)
    expect(changesOnDelete(rows, foreignKey('CustomerID', 'Orders', 'ON_DELETE_SET_NULL'))).toBe(false)
  })

  it('does not look at foreign keys that are kept', () => {
    expect(changesOnDelete(constraintRows(saved, [], []), foreignKey('CustomerID', 'Customers', 'ON_DELETE_SET_NULL'))).toBe(false)
  })
})
