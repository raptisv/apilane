import { api, unwrapAnonymous } from './api'

let required = false

/** Loaded before the first route or session; the server also enforces this gate on every request. */
export async function loadBootstrap(): Promise<void> {
  required = (await unwrapAnonymous(api.GET('/api/v1/bootstrap'))).Required
}

export function bootstrapRequired(): boolean {
  return required
}

export function completeBootstrap(): void {
  required = false
}
