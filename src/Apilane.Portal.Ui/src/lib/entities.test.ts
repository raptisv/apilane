import { describe, expect, it } from 'vitest'
import {
  constraintDescription,
  constraintLabel,
  constraintTypeText,
  dataEntityOrder,
  filterEntities,
  formatCount,
  onDeleteText,
} from './entities'
import type { Constraint, Entity } from './entities'

function entity(name: string, isSystem = false): Entity {
  return {
    Name: name,
    Description: null,
    IsSystem: isSystem,
    IsReadOnly: false,
    RequireChangeTracking: false,
    HasDifferentiationProperty: false,
    AllowPost: true,
    AllowPut: true,
    AllowDelete: true,
    AllowAddProperties: true,
    Constraints: [],
    Properties: null,
  }
}

function unique(...properties: string[]): Constraint {
  return { Type: 'Unique', IsSystem: false, Properties: properties }
}

function foreignKey(onDelete: string | null): Constraint {
  return { Type: 'ForeignKey', IsSystem: false, Properties: [], Property: 'CustomerID', ForeignEntity: 'Customers', OnDelete: onDelete }
}

describe('filterEntities', () => {
  const entities = [entity('Customers'), entity('Orders'), entity('Order_Lines')]

  it('keeps every entity for an empty search', () => {
    expect(filterEntities(entities, '  ')).toHaveLength(3)
  })

  it('matches a part of the name, ignoring case and outer spaces', () => {
    expect(filterEntities(entities, ' order ').map((e) => e.Name)).toEqual(['Orders', 'Order_Lines'])
  })

  it('returns nothing when no name matches', () => {
    expect(filterEntities(entities, 'invoice')).toEqual([])
  })
})

describe('onDeleteText', () => {
  it('puts the three rules in plain words', () => {
    expect(onDeleteText('ON_DELETE_NO_ACTION')).toBe('no action')
    expect(onDeleteText('ON_DELETE_SET_NULL')).toBe('set null')
    expect(onDeleteText('ON_DELETE_CASCADE')).toBe('cascade')
  })

  it('reads a missing rule as no action', () => {
    expect(onDeleteText(null)).toBe('no action')
    expect(onDeleteText(undefined)).toBe('no action')
  })

  it('shows an unknown rule as it is', () => {
    expect(onDeleteText('ON_DELETE_RESTRICT')).toBe('ON_DELETE_RESTRICT')
  })
})

describe('constraintTypeText', () => {
  it('names the two kinds of constraint in plain words', () => {
    expect(constraintTypeText('Unique')).toBe('Unique')
    expect(constraintTypeText('ForeignKey')).toBe('Foreign key')
  })
})

describe('constraintLabel', () => {
  it('lists the properties of a unique constraint', () => {
    expect(constraintLabel(unique('Email'))).toBe('Email')
    expect(constraintLabel(unique('Email', 'Phone'))).toBe('Email, Phone')
  })

  it('shows where a foreign key points', () => {
    expect(constraintLabel(foreignKey('ON_DELETE_CASCADE'))).toBe('CustomerID → Customers')
  })
})

describe('constraintDescription', () => {
  it('names a unique constraint', () => {
    expect(constraintDescription(unique('Email', 'Phone'))).toBe('Unique: Email, Phone')
  })

  it('says what a foreign key does on delete, in plain words', () => {
    expect(constraintDescription(foreignKey('ON_DELETE_CASCADE'))).toBe('Foreign key: CustomerID → Customers, on delete cascade')
    expect(constraintDescription(foreignKey('ON_DELETE_SET_NULL'))).toBe('Foreign key: CustomerID → Customers, on delete set null')
    expect(constraintDescription(foreignKey(null))).toBe('Foreign key: CustomerID → Customers, on delete no action')
  })
})

describe('dataEntityOrder', () => {
  it('puts the custom entities first and keeps the order within each group', () => {
    const list = [entity('Files', true), entity('Orders'), entity('Users', true), entity('Customers')]

    expect(dataEntityOrder(list).map((e) => e.Name)).toEqual(['Orders', 'Customers', 'Files', 'Users'])
  })
})

describe('formatCount', () => {
  it('groups thousands', () => {
    expect(formatCount(0)).toBe('0')
    expect(formatCount(999)).toBe('999')
    expect(formatCount(1234567)).toBe('1,234,567')
  })
})
