import { describe, expect, it } from 'vitest'
import { decimalPlacesText, limitNoun, maxLengthNote, numberOrNull, propertyRules, typeFields } from './properties'
import type { Property } from './properties'

function property(type: string, values: Partial<Property> = {}): Property {
  return {
    Name: 'Title',
    Description: null,
    Type: type,
    IsPrimaryKey: false,
    IsSystem: false,
    Position: 0,
    Required: false,
    Encrypted: false,
    ValidationRegex: null,
    DecimalPlaces: null,
    Minimum: null,
    Maximum: null,
    AllowEdit: true,
    IsUtc: false,
    AllowMin: type === 'String' || type === 'Number',
    AllowMaxEdit: type === 'Number',
    AllowValidationRegex: type === 'String',
    ...values,
  }
}

describe('typeFields', () => {
  it('gives a String its length limits, regex and encryption', () => {
    expect(typeFields('String')).toEqual({ minMax: true, decimalPlaces: false, validationRegex: true, encrypted: true })
  })

  it('gives a Number its value limits and decimal places', () => {
    expect(typeFields('Number')).toEqual({ minMax: true, decimalPlaces: true, validationRegex: false, encrypted: false })
  })

  it('gives a Boolean, a Date and no type nothing', () => {
    const none = { minMax: false, decimalPlaces: false, validationRegex: false, encrypted: false }

    expect(typeFields('Boolean')).toEqual(none)
    expect(typeFields('Date')).toEqual(none)
    expect(typeFields('')).toEqual(none)
  })
})

describe('limitNoun', () => {
  it('is a length for a String and a value otherwise', () => {
    expect(limitNoun('String')).toBe('length')
    expect(limitNoun('Number')).toBe('value')
  })
})

describe('propertyRules', () => {
  it('has nothing to say about the primary key', () => {
    expect(propertyRules(property('Number', { IsPrimaryKey: true, DecimalPlaces: 0, Minimum: 1 }))).toEqual([])
  })

  it('has nothing to say about a property without rules', () => {
    expect(propertyRules(property('String'))).toEqual([])
    expect(propertyRules(property('Boolean'))).toEqual([])
    expect(propertyRules(property('Date'))).toEqual([])
  })

  it('describes the length of a String', () => {
    expect(propertyRules(property('String', { Minimum: 2, Maximum: 50 }))).toEqual(['Length 2 to 50'])
    expect(propertyRules(property('String', { Minimum: 2 }))).toEqual(['Min length 2'])
    expect(propertyRules(property('String', { Maximum: 50 }))).toEqual(['Max length 50'])
  })

  it('keeps a limit of 0', () => {
    expect(propertyRules(property('String', { Minimum: 0 }))).toEqual(['Min length 0'])
    expect(propertyRules(property('Number', { Minimum: 0, Maximum: 0 }))).toEqual(['Min 0', 'Max 0'])
  })

  it('shows the regex of a String before its length', () => {
    expect(propertyRules(property('String', { ValidationRegex: '^[a-z]+$', Maximum: 10 }))).toEqual([
      'Matches ^[a-z]+$',
      'Max length 10',
    ])
  })

  it('leaves out a blank regex', () => {
    expect(propertyRules(property('String', { ValidationRegex: '  ' }))).toEqual([])
  })

  it('describes the decimal places and the limits of a Number', () => {
    expect(propertyRules(property('Number', { DecimalPlaces: 2, Minimum: -5, Maximum: 100 }))).toEqual([
      '2 decimal places',
      'Min -5',
      'Max 100',
    ])
    expect(propertyRules(property('Number', { DecimalPlaces: 0 }))).toEqual(['Integer'])
  })

  it('leaves out a limit too large to have arrived exactly', () => {
    // 2 ** 63 is what JSON.parse makes of the largest long, 9223372036854775807.
    expect(propertyRules(property('Number', { DecimalPlaces: 0, Minimum: 0, Maximum: 2 ** 63 }))).toEqual(['Integer', 'Min 0'])
    expect(propertyRules(property('Number', { Minimum: -(2 ** 53), Maximum: Number.MAX_SAFE_INTEGER }))).toEqual(['Max 9007199254740991'])
    expect(propertyRules(property('String', { Minimum: 2, Maximum: 2 ** 53 }))).toEqual(['Min length 2'])
  })

  it('ignores values the type has no use for', () => {
    expect(propertyRules(property('Boolean', { Minimum: 1, Maximum: 2, DecimalPlaces: 2, ValidationRegex: 'x' }))).toEqual([])
    expect(propertyRules(property('Number', { ValidationRegex: 'x' }))).toEqual([])
    expect(propertyRules(property('String', { DecimalPlaces: 2 }))).toEqual([])
  })
})

describe('decimalPlacesText', () => {
  it('reads as a person says it', () => {
    expect(decimalPlacesText(0)).toBe('Integer')
    expect(decimalPlacesText(1)).toBe('1 decimal place')
    expect(decimalPlacesText(8)).toBe('8 decimal places')
  })
})

describe('maxLengthNote', () => {
  it('warns about the string limits of MySQL and SQL Server', () => {
    expect(maxLengthNote('MySQL')).toContain('16383')
    expect(maxLengthNote('SQLServer')).toContain('4000')
  })

  it('says nothing for the other database types', () => {
    expect(maxLengthNote('SQLLite')).toBeUndefined()
    expect(maxLengthNote('PostgreSQL')).toBeUndefined()
  })
})

describe('numberOrNull', () => {
  it('reads an empty box as null', () => {
    expect(numberOrNull('')).toBeNull()
    expect(numberOrNull('  ')).toBeNull()
  })

  it('reads a number, 0 and negatives included', () => {
    expect(numberOrNull('0')).toBe(0)
    expect(numberOrNull(' -12 ')).toBe(-12)
    expect(numberOrNull(7)).toBe(7)
  })

  it('reads anything else as null', () => {
    expect(numberOrNull('abc')).toBeNull()
  })
})
