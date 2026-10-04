/**
 * Builds the body of a multipart/form-data request (a file upload) from a plain object. Use it as
 * the `bodySerializer` of an `api` call whose endpoint takes a form instead of JSON:
 *
 *   api.POST('/api/v1/applications/import', { body, bodySerializer: toFormData })
 *
 * A value that is null or undefined is left out; a number is sent as text; a File keeps its name.
 */
export function toFormData(body: object | undefined): FormData {
  const form = new FormData()

  for (const [name, value] of Object.entries(body ?? {})) {
    if (value === null || value === undefined) {
      continue
    }

    form.append(name, value instanceof Blob ? value : String(value))
  }

  return form
}
