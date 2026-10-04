import { describe, expect, it } from 'vitest'
import { connectionStringToSend, offlineWarning, statusChange } from './applicationActions'

describe('statusChange', () => {
  it('offers to take an online application offline, with the warning', () => {
    const change = statusChange('Shop', true)

    expect(change.Online).toBe(false)
    expect(change.label).toBe('Take offline')
    expect(change.title).toBe('Take Shop offline?')
    expect(change.description).toBe(offlineWarning)
    expect(change.done).toBe('Shop is offline.')
    expect(change.destructive).toBe(true)
  })

  it('offers to bring an offline application online', () => {
    const change = statusChange('Shop', false)

    expect(change.Online).toBe(true)
    expect(change.label).toBe('Bring online')
    expect(change.title).toBe('Bring Shop online?')
    expect(change.done).toBe('Shop is online.')
    expect(change.destructive).toBe(false)
  })
})

describe('connectionStringToSend', () => {
  it('sends null for an empty box, which keeps the stored value', () => {
    expect(connectionStringToSend('SQLServer', '')).toBeNull()
  })

  it('sends what was typed, spaces included, so the API can reject a blank value', () => {
    expect(connectionStringToSend('MySQL', 'Server=db;')).toBe('Server=db;')
    expect(connectionStringToSend('PostgreSQL', '   ')).toBe('   ')
  })

  it('always sends null for SQLite', () => {
    expect(connectionStringToSend('SQLLite', 'Data Source=x')).toBeNull()
  })
})
