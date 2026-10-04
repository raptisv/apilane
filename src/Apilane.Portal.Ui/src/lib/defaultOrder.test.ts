import { describe, expect, it } from 'vitest'
import { directionText, moveItem, remainingCandidates, sameOrder, toggleDirection } from './defaultOrder'
import type { SortItem } from './defaultOrder'

const items: SortItem[] = [
  { Property: 'Name', Direction: 'asc' },
  { Property: 'Created', Direction: 'desc' },
  { Property: 'ID', Direction: 'asc' },
]

function names(list: SortItem[]): string[] {
  return list.map((item) => item.Property)
}

describe('moveItem', () => {
  it('moves an item one place up or down', () => {
    expect(names(moveItem(items, 1, -1))).toEqual(['Created', 'Name', 'ID'])
    expect(names(moveItem(items, 1, 1))).toEqual(['Name', 'ID', 'Created'])
  })

  it('changes nothing at either end', () => {
    expect(names(moveItem(items, 0, -1))).toEqual(['Name', 'Created', 'ID'])
    expect(names(moveItem(items, 2, 1))).toEqual(['Name', 'Created', 'ID'])
  })

  it('leaves the list it was given alone', () => {
    moveItem(items, 0, 1)

    expect(names(items)).toEqual(['Name', 'Created', 'ID'])
  })
})

describe('toggleDirection', () => {
  it('switches one item and keeps the others', () => {
    expect(toggleDirection(items, 0).map((item) => item.Direction)).toEqual(['desc', 'desc', 'asc'])
    expect(toggleDirection(items, 1).map((item) => item.Direction)).toEqual(['asc', 'asc', 'asc'])
    expect(items[0].Direction).toBe('asc')
  })
})

describe('remainingCandidates', () => {
  it('lists the properties not sorted by yet, in the order given', () => {
    expect(remainingCandidates(['ID', 'Owner', 'Created', 'Name', 'Price'], items)).toEqual(['Owner', 'Price'])
  })
})

describe('sameOrder', () => {
  it('is true for the same properties, order and directions', () => {
    expect(sameOrder(items, items.map((item) => ({ ...item })))).toBe(true)
    expect(sameOrder([], [])).toBe(true)
  })

  it('is false after a move, a direction change, an addition or a removal', () => {
    expect(sameOrder(items, moveItem(items, 0, 1))).toBe(false)
    expect(sameOrder(items, toggleDirection(items, 2))).toBe(false)
    expect(sameOrder(items, [...items, { Property: 'Price', Direction: 'asc' }])).toBe(false)
    expect(sameOrder(items, items.slice(1))).toBe(false)
  })
})

describe('directionText', () => {
  it('puts the direction in plain words', () => {
    expect(directionText('asc')).toBe('ascending')
    expect(directionText('desc')).toBe('descending')
  })
})
