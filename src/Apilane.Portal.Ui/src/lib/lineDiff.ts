// A line by line comparison of two texts, for showing what a save changes (the SQL of a custom
// endpoint). No library: the texts are short and the comparison is the plain longest common
// subsequence of their lines.

export type DiffLineKind = 'same' | 'added' | 'removed' | 'gap'

export interface DiffLine {
  kind: DiffLineKind
  /** The text of the line. Empty for a gap. */
  text: string
  /** The line number in the old text (not on an added line or a gap). */
  oldNumber?: number
  /** The line number in the new text (not on a removed line or a gap). */
  newNumber?: number
  /** How many unchanged lines a gap stands for. */
  skipped?: number
}

// The comparison needs a table of (old lines x new lines) cells. Past this many cells the middle part is
// shown as removed and added as a whole: a wrong but readable answer beats a frozen browser.
const MaxCells = 4_000_000

function splitLines(text: string): string[] {
  return text.split(/\r\n|\r|\n/)
}

/**
 * The lines of the new text with what was removed from the old one, in reading order: inside a changed
 * block the removed lines come before the added ones.
 */
export function diffLines(before: string, after: string): DiffLine[] {
  const a = splitLines(before)
  const b = splitLines(after)

  // The lines both texts start and end with need no comparison.
  let start = 0
  while (start < a.length && start < b.length && a[start] === b[start]) {
    start++
  }

  let endA = a.length
  let endB = b.length
  while (endA > start && endB > start && a[endA - 1] === b[endB - 1]) {
    endA--
    endB--
  }

  const result: DiffLine[] = []

  for (let i = 0; i < start; i++) {
    result.push({ kind: 'same', text: a[i], oldNumber: i + 1, newNumber: i + 1 })
  }

  const middleA = a.slice(start, endA)
  const middleB = b.slice(start, endB)
  const cells = (middleA.length + 1) * (middleB.length + 1)

  if (cells > MaxCells) {
    middleA.forEach((text, i) => result.push({ kind: 'removed', text, oldNumber: start + i + 1 }))
    middleB.forEach((text, j) => result.push({ kind: 'added', text, newNumber: start + j + 1 }))
  } else {
    result.push(...compareMiddle(middleA, middleB, start))
  }

  const tail = a.length - endA
  for (let k = 0; k < tail; k++) {
    result.push({ kind: 'same', text: a[endA + k], oldNumber: endA + k + 1, newNumber: endB + k + 1 })
  }

  return result
}

// The longest common subsequence of the two blocks, as lines. `offset` is the number of lines above them.
function compareMiddle(a: string[], b: string[], offset: number): DiffLine[] {
  const rows = a.length + 1
  const columns = b.length + 1
  // lcs[i * columns + j] is the length of the longest common subsequence of a[i..] and b[j..].
  const lcs = new Uint32Array(rows * columns)

  for (let i = a.length - 1; i >= 0; i--) {
    for (let j = b.length - 1; j >= 0; j--) {
      lcs[i * columns + j] =
        a[i] === b[j] ? lcs[(i + 1) * columns + j + 1] + 1 : Math.max(lcs[(i + 1) * columns + j], lcs[i * columns + j + 1])
    }
  }

  const result: DiffLine[] = []
  let i = 0
  let j = 0

  while (i < a.length && j < b.length) {
    if (a[i] === b[j]) {
      result.push({ kind: 'same', text: a[i], oldNumber: offset + i + 1, newNumber: offset + j + 1 })
      i++
      j++
    } else if (lcs[(i + 1) * columns + j] >= lcs[i * columns + j + 1]) {
      result.push({ kind: 'removed', text: a[i], oldNumber: offset + i + 1 })
      i++
    } else {
      result.push({ kind: 'added', text: b[j], newNumber: offset + j + 1 })
      j++
    }
  }

  for (; i < a.length; i++) {
    result.push({ kind: 'removed', text: a[i], oldNumber: offset + i + 1 })
  }

  for (; j < b.length; j++) {
    result.push({ kind: 'added', text: b[j], newNumber: offset + j + 1 })
  }

  return result
}

/** True when the two texts differ in at least one line. */
export function hasChanges(lines: readonly DiffLine[]): boolean {
  return lines.some((line) => line.kind === 'added' || line.kind === 'removed')
}

/**
 * Keeps `context` unchanged lines around every change and replaces each longer run of unchanged lines
 * by one gap line that says how many it hides.
 */
export function collapseUnchanged(lines: readonly DiffLine[], context = 3): DiffLine[] {
  const keep = new Array<boolean>(lines.length).fill(false)

  lines.forEach((line, index) => {
    if (line.kind === 'added' || line.kind === 'removed') {
      for (let k = Math.max(0, index - context); k <= Math.min(lines.length - 1, index + context); k++) {
        keep[k] = true
      }
    }
  })

  const result: DiffLine[] = []
  let hidden = 0

  lines.forEach((line, index) => {
    if (keep[index]) {
      if (hidden > 0) {
        result.push({ kind: 'gap', text: '', skipped: hidden })
        hidden = 0
      }
      result.push(line)
    } else {
      hidden++
    }
  })

  // Unchanged lines at the very end are shown as a gap only when something changed above them.
  if (hidden > 0 && hidden < lines.length) {
    result.push({ kind: 'gap', text: '', skipped: hidden })
  }

  return result
}
