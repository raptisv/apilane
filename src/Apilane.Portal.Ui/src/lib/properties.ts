import type { Schemas } from './api'

/**
 * How the properties of an entity are described and which fields a property form shows, for the
 * properties screen. Pure functions, no Vue, so they can be unit-tested (properties.test.ts).
 */
export type Property = Schemas['PropertyResponse']

/** The Type values of the API, in the order the create form offers them. */
export const propertyTypes: readonly string[] = ['String', 'Number', 'Boolean', 'Date']

/** The fields of the create form that depend on the type. Name, description and Required are always there. */
export interface TypeFields {
  /** Minimum and Maximum: a length for a String, a value for a Number. */
  minMax: boolean
  decimalPlaces: boolean
  validationRegex: boolean
  encrypted: boolean
}

/** Which type-dependent fields a new property of `type` uses. An unknown or empty type uses none. */
export function typeFields(type: string): TypeFields {
  return {
    minMax: type === 'String' || type === 'Number',
    decimalPlaces: type === 'Number',
    validationRegex: type === 'String',
    encrypted: type === 'String',
  }
}

/** What Minimum and Maximum limit for the type: the 'length' of a String, the 'value' of a Number. */
export function limitNoun(type: string): string {
  return type === 'String' ? 'length' : 'value'
}

/**
 * The rules of a property in plain words, for the properties list: 'Length 2 to 50',
 * 'Matches ^[a-z]+$', '2 decimal places', 'Min 0'. The primary key has none. Required and
 * Encrypted are not here: the list shows them as badges next to the name.
 */
export function propertyRules(property: Property): string[] {
  if (property.IsPrimaryKey) {
    return []
  }

  const rules: string[] = []
  const min = exactOrUndefined(property.Minimum)
  const max = exactOrUndefined(property.Maximum)

  if (property.Type === 'Number' && property.DecimalPlaces != null) {
    rules.push(decimalPlacesText(property.DecimalPlaces))
  }

  if (property.Type === 'String') {
    if (property.ValidationRegex?.trim()) {
      rules.push(`Matches ${property.ValidationRegex}`)
    }

    if (min !== undefined && max !== undefined) {
      rules.push(`Length ${min} to ${max}`)
    } else if (min !== undefined) {
      rules.push(`Min length ${min}`)
    } else if (max !== undefined) {
      rules.push(`Max length ${max}`)
    }
  }

  if (property.Type === 'Number') {
    if (min !== undefined) {
      rules.push(`Min ${min}`)
    }

    if (max !== undefined) {
      rules.push(`Max ${max}`)
    }
  }

  return rules
}

/**
 * A limit as the list may print it. One beyond the whole numbers JavaScript holds exactly arrives
 * rounded (the 'no limit' maximum of a differentiation property is the largest long), and a wrong
 * number is worse than none, so it is left out.
 */
function exactOrUndefined(limit: number | null | undefined): number | undefined {
  return limit != null && Number.isSafeInteger(limit) ? limit : undefined
}

/** 0 -> 'Integer', 1 -> '1 decimal place', 2 -> '2 decimal places'. */
export function decimalPlacesText(decimalPlaces: number): string {
  if (decimalPlaces === 0) {
    return 'Integer'
  }

  return decimalPlaces === 1 ? '1 decimal place' : `${decimalPlaces} decimal places`
}

/**
 * What the form says under the maximum length of a new String, for the database types that limit
 * the size of a string column. Undefined for the others.
 */
export function maxLengthNote(databaseType: string): string | undefined {
  if (databaseType === 'MySQL') {
    return 'In MySQL the maximum string length is 16383. If you need more, leave this empty. Large strings cannot be used in unique constraints.'
  }

  if (databaseType === 'SQLServer') {
    return 'In SQL Server the maximum string length is 4000. If you need more, leave this empty. Strings longer than 4000 characters cannot be used in unique constraints.'
  }

  return undefined
}

/** The value of a number box as the API takes it: null for an empty box. */
export function numberOrNull(text: string | number): number | null {
  const trimmed = String(text).trim()

  if (trimmed === '') {
    return null
  }

  const value = Number(trimmed)

  return Number.isNaN(value) ? null : value
}
