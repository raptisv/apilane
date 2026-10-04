import { shallowRef } from 'vue'
import type { ShallowRef } from 'vue'
import { api, ApiError, unwrap, unwrapAnonymous } from './api'
import type { Schemas } from './api'

export type Session = Schemas['SessionResponse']

const session = shallowRef<Session>()

/**
 * Loads the signed-in user again, after something the session shows has changed (the instance
 * name, the password). If the session has ended, the browser goes to the login page.
 */
export async function loadSession(): Promise<void> {
  session.value = await unwrap(api.GET('/api/v1/session'))
}

/**
 * Loads the signed-in user if there is one. Called once, before the app is shown. Returns false
 * when nobody is signed in: the router then sends every screen that needs a session to the login page.
 */
export async function tryLoadSession(): Promise<boolean> {
  try {
    session.value = await unwrapAnonymous(api.GET('/api/v1/session'))
    return true
  } catch (error) {
    if (error instanceof ApiError && error.status === 401) {
      session.value = undefined
      return false
    }

    throw error
  }
}

/** Stores the session that signing in or registering returned. */
export function setSession(value: Session): void {
  session.value = value
}

/** The signed-in user, or undefined when nobody is signed in. For code that also runs on public screens. */
export function currentSession(): Session | undefined {
  return session.value
}

/**
 * The signed-in user. Always set on a screen inside AppShell: the router lets nobody in without a
 * session. Public screens (inside AuthLayout) must not call it.
 */
export function useSession(): Readonly<ShallowRef<Session>> {
  if (!session.value) {
    throw new Error('useSession() was called on a screen without a session.')
  }

  return session as ShallowRef<Session>
}
