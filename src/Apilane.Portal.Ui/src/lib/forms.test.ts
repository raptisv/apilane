import { describe, expect, it } from 'vitest'
import { ApiError } from './api'
import { listErrors } from './forms'

function validation(...errors: [string, string][]): ApiError {
  return new ApiError(400, {
    Code: 'VALIDATION',
    Message: 'The request is not valid.',
    Errors: errors.map(([Property, Message]) => ({ Property, Message })),
  })
}

describe('listErrors', () => {
  it('has nothing to show without an error', () => {
    expect(listErrors(undefined, 'Constraints')).toEqual({ rows: {}, message: undefined })
  })

  it('puts each problem at the place of its item', () => {
    const errors = listErrors(
      validation(['Constraints[1].Properties', "Property 'Email' does not exist"], ['Constraints[0].OnDelete', 'Required']),
      'Constraints',
    )

    expect(errors.rows).toEqual({ 0: 'Required', 1: "Property 'Email' does not exist" })
    expect(errors.message).toBeUndefined()
  })

  it('joins several problems of one item', () => {
    const errors = listErrors(validation(['Items[2].Property', 'First.'], ['Items[2]', 'Second.']), 'Items')

    expect(errors.rows).toEqual({ 2: 'First. Second.' })
  })

  it('shows a problem that is not about one item above the list', () => {
    const errors = listErrors(validation(['Constraints', 'Required'], ['Items[0]', 'Another list']), 'Constraints')

    expect(errors.rows).toEqual({})
    expect(errors.message).toBe('Required Another list')
  })

  it('shows the message of an error without details above the list', () => {
    expect(listErrors(new ApiError(502, { Code: 'UPSTREAM_ERROR', Message: 'The API server did not answer.' }), 'Items')).toEqual({
      rows: {},
      message: 'The API server did not answer.',
    })
    expect(listErrors(new Error('Offline'), 'Items').message).toBe('Offline')
  })
})
