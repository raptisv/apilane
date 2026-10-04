import { ApiError } from './api'
import type { Schemas } from './api'

/**
 * The rules of the schema import screen: checking the text of the payload box before it is sent,
 * counting what a payload holds, and reading a failed import. Pure functions, no Vue, so they can
 * be unit-tested (schemaImport.test.ts).
 */
export type SchemaImportPayload = Schemas['SchemaImportRequest']

/** What the payload box holds: a payload to send, or the reason it cannot be sent. */
export type ParsedPayload = { payload: SchemaImportPayload; error?: undefined } | { payload?: undefined; error: string }

const lists = ['Entities', 'Security', 'CustomEndpoints'] as const

/**
 * Reads the text of the payload box. Only what the browser can tell is checked: the text is JSON,
 * it is an object, and its three lists are lists. Everything else is the API's to judge.
 */
export function parsePayload(text: string): ParsedPayload {
  if (text.trim() === '') {
    return { error: 'Paste a JSON payload first.' }
  }

  let value: unknown

  try {
    value = JSON.parse(text)
  } catch (e) {
    return { error: `Invalid JSON: ${e instanceof Error ? e.message : String(e)}` }
  }

  if (typeof value !== 'object' || value === null || Array.isArray(value)) {
    return { error: 'The payload must be a JSON object: {"Entities": [], "Security": [], "CustomEndpoints": []}' }
  }

  for (const name of lists) {
    const list = (value as Record<string, unknown>)[name]

    // A list that is left out or null means none.
    if (list !== undefined && list !== null && !Array.isArray(list)) {
      return { error: `'${name}' must be a list: "${name}": []` }
    }
  }

  return { payload: value as SchemaImportPayload }
}

/** One badge of the summary: '2 new entities'. */
export interface SummaryCount {
  count: number
  label: string
}

/**
 * What a payload holds, as the badges shown after 'Load diff': new entities, entities that get
 * properties or constraints, the properties and constraints of all of them, security rules and
 * custom endpoints. A count of zero is left out, so a payload with nothing to import gives no badges.
 */
export function payloadSummary(payload: SchemaImportPayload): SummaryCount[] {
  const entities = payload.Entities ?? []
  const newEntities = entities.filter((entity) => entity.IsNew === true).length
  const properties = entities.reduce((sum, entity) => sum + (entity.Properties?.length ?? 0), 0)
  // The import skips a constraint without properties.
  const constraints = entities.reduce(
    (sum, entity) => sum + (entity.Constraints ?? []).filter((constraint) => constraint.Properties?.trim()).length,
    0,
  )

  return [
    count(newEntities, 'new entity', 'new entities'),
    count(entities.length - newEntities, 'entity updated', 'entities updated'),
    count(properties, 'property', 'properties'),
    count(constraints, 'constraint', 'constraints'),
    count(payload.Security?.length ?? 0, 'security rule', 'security rules'),
    count(payload.CustomEndpoints?.length ?? 0, 'custom endpoint', 'custom endpoints'),
  ].filter((item) => item.count > 0)
}

function count(value: number, one: string, many: string): SummaryCount {
  return { count: value, label: value === 1 ? one : many }
}

/** What the screen shows for an import that failed. */
export interface ImportFailure {
  /** What went wrong. For a step that failed it names the step and says the earlier steps stay applied. */
  message: string
  /** Where in the payload, as the API names it ('Entities[0].Properties[1].TypeID'), with what is wrong there. */
  places: { place: string; message: string | undefined }[]
  /** Finds the request in the Portal logs. */
  traceId: string | undefined
}

export function importFailure(error: Error): ImportFailure {
  if (!(error instanceof ApiError)) {
    return { message: error.message, places: [], traceId: undefined }
  }

  return {
    message: error.message,
    places: error.errors.map((detail) => ({
      place: detail.Property,
      // A failed step repeats its text in the message: it is shown once.
      message: error.message.startsWith(detail.Message) ? undefined : detail.Message,
    })),
    traceId: error.traceId,
  }
}

/** The example of the classic Import schema page: two entities, a foreign key, a security rule and a custom endpoint. */
export const examplePayload = JSON.stringify(
  {
    Entities: [
      {
        Name: 'Product',
        Description: 'Product catalog',
        RequireChangeTracking: false,
        HasDifferentiationProperty: false,
        Properties: [
          {
            Name: 'Title',
            TypeID: 1,
            Required: true,
            Minimum: null,
            Maximum: 200,
            DecimalPlaces: null,
            Encrypted: false,
            ValidationRegex: null,
            Description: null,
          },
          {
            Name: 'Price',
            TypeID: 2,
            Required: true,
            Minimum: 0,
            Maximum: null,
            DecimalPlaces: 2,
            Encrypted: false,
            ValidationRegex: null,
            Description: null,
          },
        ],
        Constraints: [],
      },
      {
        Name: 'OrderItem',
        Description: 'Line item in an order',
        RequireChangeTracking: false,
        HasDifferentiationProperty: false,
        Properties: [
          {
            Name: 'Product_ID',
            TypeID: 2,
            Required: true,
            Minimum: null,
            Maximum: null,
            DecimalPlaces: 0,
            Encrypted: false,
            ValidationRegex: null,
            Description: null,
          },
        ],
        Constraints: [{ TypeID: 2, Properties: 'Product_ID,Product' }],
      },
    ],
    Security: [
      {
        Name: 'Product',
        TypeID: 0,
        RoleID: 'ANONYMOUS',
        Action: 'get',
        Record: 0,
        Properties: null,
        RateLimit: null,
      },
    ],
    CustomEndpoints: [
      {
        Name: 'GetAllProduct',
        Description: 'Retrieves all products.',
        Query: 'SELECT * FROM [Product];',
      },
    ],
  },
  null,
  2,
)
