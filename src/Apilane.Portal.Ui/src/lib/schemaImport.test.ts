import { describe, expect, it } from 'vitest'
import { ApiError } from './api'
import { examplePayload, importFailure, parsePayload, payloadSummary } from './schemaImport'

describe('parsePayload', () => {
  it('asks for a payload when the box is empty or blank', () => {
    expect(parsePayload('')).toEqual({ error: 'Paste a JSON payload first.' })
    expect(parsePayload('  \n ')).toEqual({ error: 'Paste a JSON payload first.' })
  })

  it('reports text that is not JSON with the reason', () => {
    const result = parsePayload('{"Entities": [}')

    expect(result.payload).toBeUndefined()
    expect(result.error).toMatch(/^Invalid JSON: .+/)
  })

  it('refuses JSON that is not an object', () => {
    for (const text of ['[]', '5', '"text"', 'null', 'true']) {
      expect(parsePayload(text).error).toMatch(/^The payload must be a JSON object/)
    }
  })

  it('refuses a list that is not a list, by name', () => {
    expect(parsePayload('{"Entities": {}}').error).toBe(`'Entities' must be a list: "Entities": []`)
    expect(parsePayload('{"Security": "all"}').error).toBe(`'Security' must be a list: "Security": []`)
    expect(parsePayload('{"CustomEndpoints": 1}').error).toBe(`'CustomEndpoints' must be a list: "CustomEndpoints": []`)
  })

  it('accepts lists that are left out or null', () => {
    expect(parsePayload('{}')).toEqual({ payload: {} })
    expect(parsePayload('{"Entities": null, "Security": []}')).toEqual({ payload: { Entities: null, Security: [] } })
  })

  it('returns the payload as it was written, unknown values included', () => {
    const text = '{"Entities": [{"Name": "Orders", "TypeID": "x"}], "Other": 1}'

    expect(parsePayload(text).payload).toEqual(JSON.parse(text))
  })

  it('accepts the example payload', () => {
    expect(parsePayload(examplePayload).error).toBeUndefined()
  })
})

describe('payloadSummary', () => {
  it('has no badges for a payload with nothing in it', () => {
    expect(payloadSummary({})).toEqual([])
    expect(payloadSummary({ Entities: [], Security: null, CustomEndpoints: [] })).toEqual([])
  })

  it('counts new entities apart from the ones that only get additions', () => {
    const summary = payloadSummary({
      Entities: [
        { Name: 'Orders', IsNew: true },
        { Name: 'Items', IsNew: true },
        { Name: 'Customers', IsNew: false },
      ],
    })

    expect(summary).toEqual([
      { count: 2, label: 'new entities' },
      { count: 1, label: 'entity updated' },
    ])
  })

  it('counts an entity without IsNew (a payload written by hand) as updated', () => {
    expect(payloadSummary({ Entities: [{ Name: 'Orders' }, { Name: 'Items' }] })).toEqual([
      { count: 2, label: 'entities updated' },
    ])
  })

  it('adds up the properties and constraints of all entities', () => {
    const summary = payloadSummary({
      Entities: [
        {
          Name: 'Orders',
          IsNew: true,
          Properties: [{ Name: 'Code' }, { Name: 'Total' }],
          Constraints: [{ TypeID: 1, Properties: 'Code' }],
        },
        { Name: 'Items', IsNew: true, Properties: [{ Name: 'Order_ID' }], Constraints: null },
      ],
    })

    expect(summary).toEqual([
      { count: 2, label: 'new entities' },
      { count: 3, label: 'properties' },
      { count: 1, label: 'constraint' },
    ])
  })

  it('leaves out a constraint without properties, as the import does', () => {
    const summary = payloadSummary({
      Entities: [
        {
          Name: 'Orders',
          IsNew: true,
          Constraints: [{ TypeID: 1, Properties: '  ' }, { TypeID: 1, Properties: null }, { TypeID: 1 }],
        },
      ],
    })

    expect(summary).toEqual([{ count: 1, label: 'new entity' }])
  })

  it('counts security rules and custom endpoints, singular and plural', () => {
    const rule = { Name: 'Orders', RoleID: 'ANONYMOUS', Action: 'get' }
    const endpoint = { Name: 'GetOrders', Query: 'SELECT 1' }

    expect(payloadSummary({ Security: [rule], CustomEndpoints: [endpoint, endpoint] })).toEqual([
      { count: 1, label: 'security rule' },
      { count: 2, label: 'custom endpoints' },
    ])
  })

  it('counts the example payload', () => {
    const { payload } = parsePayload(examplePayload)

    expect(payload && payloadSummary(payload)).toEqual([
      { count: 2, label: 'entities updated' },
      { count: 3, label: 'properties' },
      { count: 1, label: 'constraint' },
      { count: 1, label: 'security rule' },
      { count: 1, label: 'custom endpoint' },
    ])
  })
})

describe('importFailure', () => {
  it('shows the message of an error that is not from the API', () => {
    expect(importFailure(new Error('Something broke.'))).toEqual({ message: 'Something broke.', places: [], traceId: undefined })
  })

  it('names the place of a failed step without repeating its text', () => {
    const problem = "Property 'Phone': 'TypeID' mismatch (existing: 1, import: 2)."
    const failure = importFailure(
      new ApiError(400, {
        Code: 'VALIDATION',
        Message: `${problem} The import stopped at this step; the steps before it stay applied.`,
        Property: 'Entities[0].Properties[1].TypeID',
        Errors: [{ Property: 'Entities[0].Properties[1].TypeID', Message: problem }],
        TraceId: 'abc',
      }),
    )

    expect(failure).toEqual({
      message: `${problem} The import stopped at this step; the steps before it stay applied.`,
      places: [{ place: 'Entities[0].Properties[1].TypeID', message: undefined }],
      traceId: 'abc',
    })
  })

  it('lists every problem of a payload the API refused before applying anything', () => {
    const failure = importFailure(
      new ApiError(400, {
        Code: 'VALIDATION',
        Message: 'The request is not valid.',
        Errors: [
          { Property: 'Entities[0].Name', Message: 'Required' },
          { Property: 'Security[1].Action', Message: 'Required' },
        ],
      }),
    )

    expect(failure.message).toBe('The request is not valid.')
    expect(failure.places).toEqual([
      { place: 'Entities[0].Name', message: 'Required' },
      { place: 'Security[1].Action', message: 'Required' },
    ])
  })

  it('has no places for a failure of the API server', () => {
    const failure = importFailure(
      new ApiError(502, { Code: 'UPSTREAM_ERROR', Message: "Creating entity 'Orders' on the API server: no answer.", TraceId: 't1' }),
    )

    expect(failure).toEqual({ message: "Creating entity 'Orders' on the API server: no answer.", places: [], traceId: 't1' })
  })
})
