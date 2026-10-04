import { describe, expect, it } from 'vitest'
import { csvFileName, toCsv } from './csv'

describe('toCsv', () => {
  it('quotes every field and ends rows with CRLF', () => {
    expect(toCsv(['ID', 'Name'], [['1', 'Anna'], ['2', 'Ben']])).toBe('"ID","Name"\r\n"1","Anna"\r\n"2","Ben"')
  })

  it('doubles every double quote, not only the first', () => {
    expect(toCsv(['A'], [['say "hi" and "bye"']])).toBe('"A"\r\n"say ""hi"" and ""bye"""')
  })

  it('keeps commas, line breaks and # inside the cell', () => {
    expect(toCsv(['A', 'B'], [['one, two', 'line 1\r\nline 2 #3']])).toBe('"A","B"\r\n"one, two","line 1\r\nline 2 #3"')
  })

  it('writes a null as an empty cell', () => {
    expect(toCsv(['A', 'B'], [[null, 'x']])).toBe('"A","B"\r\n"","x"')
  })

  it('writes only the header row for an empty page', () => {
    expect(toCsv(['ID'], [])).toBe('"ID"')
  })
})

describe('csvFileName', () => {
  it('names the file after the entity and the local time', () => {
    expect(csvFileName('Orders', new Date(2026, 9, 3, 14, 5, 9))).toBe('Orders_2026_10_03_14_05_09.csv')
  })
})
