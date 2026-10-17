import { describe, expect, it } from 'vitest'
import { searchUsers, userCountText } from './adminUsers'
import type { AdminUser } from './adminUsers'

function user(email: string): AdminUser {
  return { ID: email, Email: email, IsAdmin: false, IsCurrentUser: false, LastLogin: '2026-10-01T12:00:00Z' }
}

const all = [user('anna@example.com'), user('Bob@Example.com'), user('carl@other.test')]

function emails(users: AdminUser[]): string[] {
  return users.map((entry) => entry.Email)
}

describe('searchUsers', () => {
  it('keeps every user, in the same order, for an empty search', () => {
    expect(emails(searchUsers(all, ''))).toEqual(['anna@example.com', 'Bob@Example.com', 'carl@other.test'])
    expect(emails(searchUsers(all, '   '))).toEqual(['anna@example.com', 'Bob@Example.com', 'carl@other.test'])
  })

  it('finds by any part of the e-mail, ignoring case and the spaces around the search', () => {
    expect(emails(searchUsers(all, 'bob@'))).toEqual(['Bob@Example.com'])
    expect(emails(searchUsers(all, ' EXAMPLE.COM '))).toEqual(['anna@example.com', 'Bob@Example.com'])
  })

  it('returns nothing when no user matches', () => {
    expect(searchUsers(all, 'nobody')).toEqual([])
  })
})

describe('userCountText', () => {
  it('says how many there are', () => {
    expect(userCountText(240, 240)).toBe('240 users')
    expect(userCountText(1, 1)).toBe('1 user')
  })

  it('says how many of them a search shows', () => {
    expect(userCountText(3, 240)).toBe('3 of 240 users')
    expect(userCountText(0, 1)).toBe('0 of 1 user')
  })
})
