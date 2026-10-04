import { ApiError } from './api'

/** What a form shows for a failed write: a message under each field, and one above the form. */
export interface FormErrors {
  /** Field name (the property name of the request, for example 'ServerUrl') to its message. */
  fields: Record<string, string>
  /** Shown above the form when the error is not about one of its fields. */
  message: string | undefined
}

/**
 * Turns the error of a failed write into what a form shows. Use it with useMutation:
 *
 *   const errors = computed(() => formErrors(save.error.value, ['Name', 'ServerUrl']))
 *
 * `fieldNames` are the fields the form renders. A problem with any other property has no field to
 * appear under, so it goes into the form-level message instead of being lost.
 */
export function formErrors(error: Error | undefined, fieldNames: readonly string[]): FormErrors {
  const fields: Record<string, string> = {}

  if (!error) {
    return { fields, message: undefined }
  }

  if (!(error instanceof ApiError)) {
    return { fields, message: error.message }
  }

  // A VALIDATION error lists its problems; other errors may name a single property.
  let details = error.errors

  if (details.length === 0 && error.property) {
    details = [{ Property: error.property, Message: error.message }]
  }

  if (details.length === 0) {
    return { fields, message: error.message }
  }

  const other: string[] = []

  for (const detail of details) {
    if (!fieldNames.includes(detail.Property)) {
      other.push(detail.Message)
    } else if (!fields[detail.Property]) {
      // One message per field: the first. The next one shows once this one is fixed.
      fields[detail.Property] = detail.Message
    }
  }

  return { fields, message: other.length > 0 ? other.join(' ') : undefined }
}

/** What a list editor shows for a failed write: a message at each row, and one above the list. */
export interface ListErrors {
  /** Place of the item in the request list to its messages. */
  rows: Record<number, string>
  /** Shown above the list: everything that is not about one item. */
  message: string | undefined
}

/**
 * Turns the error of a failed write into what a list editor shows, for a request that is one list
 * (the constraints or the default sorting of an entity). The API names the place of each problem,
 * 'Constraints[1].Properties'; `listName` is the part before the bracket.
 *
 *   const errors = computed(() => listErrors(save.error.value, 'Constraints'))
 */
export function listErrors(error: Error | undefined, listName: string): ListErrors {
  const rows: Record<number, string> = {}

  if (!error) {
    return { rows, message: undefined }
  }

  const details = error instanceof ApiError ? error.errors : []

  if (details.length === 0) {
    return { rows, message: error.message }
  }

  const prefix = `${listName}[`
  const other: string[] = []

  for (const detail of details) {
    const place = detail.Property.startsWith(prefix) ? parseInt(detail.Property.slice(prefix.length), 10) : NaN

    if (Number.isNaN(place)) {
      other.push(detail.Message)
    } else {
      rows[place] = rows[place] ? `${rows[place]} ${detail.Message}` : detail.Message
    }
  }

  return { rows, message: other.length > 0 ? other.join(' ') : undefined }
}
