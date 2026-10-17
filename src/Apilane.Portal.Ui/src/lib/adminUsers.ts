import type { Schemas } from './api'

/**
 * The rules of the administrator's Users list (the people of the instance). Pure functions, no
 * Vue, so they can be unit-tested (adminUsers.test.ts).
 */
export type AdminUser = Schemas['UserResponse']

/** The users whose e-mail contains `search`, ignoring case. An empty search keeps them all, in the same order. */
export function searchUsers(users: readonly AdminUser[], search: string): AdminUser[] {
  const text = search.trim().toLowerCase()

  return users.filter((user) => user.Email.toLowerCase().includes(text))
}

/** How many users the list shows: '240 users', or '3 of 240 users' while a search hides some. */
export function userCountText(shown: number, total: number): string {
  const noun = total === 1 ? 'user' : 'users'

  return shown === total ? `${total} ${noun}` : `${shown} of ${total} ${noun}`
}
