import { describe, expect, it } from 'vitest'
import { collapseUnchanged, diffLines, hasChanges } from './lineDiff'

const kinds = (before: string, after: string) => diffLines(before, after).map((line) => `${line.kind}:${line.text}`)

describe('diffLines', () => {
  it('has no change for equal texts', () => {
    const lines = diffLines('select 1\nfrom a', 'select 1\nfrom a')

    expect(hasChanges(lines)).toBe(false)
    expect(lines.map((line) => line.kind)).toEqual(['same', 'same'])
  })

  it('shows a changed line as the removed one, then the added one', () => {
    expect(kinds('select 1\nfrom a\nwhere x', 'select 1\nfrom b\nwhere x')).toEqual([
      'same:select 1',
      'removed:from a',
      'added:from b',
      'same:where x',
    ])
  })

  it('shows lines that were added and removed in different places', () => {
    expect(kinds('a\nb\nc\nd', 'a\nc\nd\ne')).toEqual(['same:a', 'removed:b', 'same:c', 'same:d', 'added:e'])
  })

  it('numbers lines of the old and of the new text', () => {
    const lines = diffLines('a\nb\nc', 'a\nx\ny\nc')

    expect(lines.map((line) => [line.kind, line.oldNumber, line.newNumber])).toEqual([
      ['same', 1, 1],
      ['removed', 2, undefined],
      ['added', undefined, 2],
      ['added', undefined, 3],
      ['same', 3, 4],
    ])
  })

  it('treats the line endings of Windows, Unix and old Mac alike', () => {
    expect(hasChanges(diffLines('a\r\nb\rc\nd', 'a\nb\nc\nd'))).toBe(false)
  })

  it('compares an empty text with a text', () => {
    expect(kinds('', 'select 1')).toEqual(['removed:', 'added:select 1'])
    expect(kinds('select 1', '')).toEqual(['removed:select 1', 'added:'])
  })

  it('catches a change in the whitespace of a line', () => {
    expect(hasChanges(diffLines('select  1', 'select 1'))).toBe(true)
  })

  it('still answers for texts too large to compare line by line', () => {
    const before = Array.from({ length: 2500 }, (_, i) => `a${i}`).join('\n')
    const after = Array.from({ length: 2500 }, (_, i) => `b${i}`).join('\n')
    const lines = diffLines(before, after)

    expect(lines.filter((line) => line.kind === 'removed')).toHaveLength(2500)
    expect(lines.filter((line) => line.kind === 'added')).toHaveLength(2500)
  })
})

describe('collapseUnchanged', () => {
  const numbered = (count: number) => Array.from({ length: count }, (_, i) => `l${i + 1}`)

  it('hides long unchanged runs and keeps the context of a change', () => {
    const before = numbered(20).join('\n')
    const after = numbered(20).map((line) => (line === 'l10' ? 'changed' : line)).join('\n')
    const lines = collapseUnchanged(diffLines(before, after), 2)

    expect(lines.map((line) => (line.kind === 'gap' ? `gap:${line.skipped}` : `${line.kind}:${line.text}`))).toEqual([
      'gap:7',
      'same:l8',
      'same:l9',
      'removed:l10',
      'added:changed',
      'same:l11',
      'same:l12',
      'gap:8',
    ])
  })

  it('shows everything when the changes are close together', () => {
    const lines = collapseUnchanged(diffLines('a\nb\nc', 'a\nx\nc'), 3)

    expect(lines.some((line) => line.kind === 'gap')).toBe(false)
  })

  it('has no gap when nothing changed', () => {
    expect(collapseUnchanged(diffLines('a\nb', 'a\nb'))).toEqual([])
  })
})
