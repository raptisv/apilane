import type { Schemas } from './api'

/**
 * The editing rules of the default sorting screen. Pure functions, no Vue (defaultOrder.test.ts).
 * Each returns a new list and leaves the one it was given alone.
 */
export type SortItem = Schemas['DefaultOrderItem']

/** The item at `index` moved one place up (-1) or down (1). A move past either end changes nothing. */
export function moveItem(items: readonly SortItem[], index: number, step: -1 | 1): SortItem[] {
  const result = [...items]
  const target = index + step

  if (index >= 0 && index < result.length && target >= 0 && target < result.length) {
    ;[result[index], result[target]] = [result[target], result[index]]
  }

  return result
}

/** The item at `index` with its direction switched between 'asc' and 'desc'. */
export function toggleDirection(items: readonly SortItem[], index: number): SortItem[] {
  return items.map((item, i) => (i === index ? { ...item, Direction: item.Direction === 'asc' ? 'desc' : 'asc' } : item))
}

/** The properties that are not sorted by yet, in the order the API lists them. */
export function remainingCandidates(candidates: readonly string[], items: readonly SortItem[]): string[] {
  return candidates.filter((name) => !items.some((item) => item.Property === name))
}

/** True when both lists sort by the same properties, in the same order and direction. */
export function sameOrder(a: readonly SortItem[], b: readonly SortItem[]): boolean {
  return a.length === b.length && a.every((item, i) => item.Property === b[i].Property && item.Direction === b[i].Direction)
}

/** 'asc' in plain words. */
export function directionText(direction: string): string {
  return direction === 'asc' ? 'ascending' : 'descending'
}
