import { api, unwrapAnonymous } from '@/lib/api'
import { useAsync } from './useAsync'

function load() {
  return useAsync(() => unwrapAnonymous(api.GET('/api/v1/instance')))
}

let shared: ReturnType<typeof load> | undefined

/**
 * What the Portal tells a visitor who is not signed in: the instance name, whether registration
 * is open and whether mail is set up. Use it on public screens (AuthLayout and its pages); a
 * screen with a session reads the name from useSession() instead.
 * It is loaded once, on first use, and shared: every caller gets the same useAsync state.
 */
export function useInstance(): ReturnType<typeof load> {
  shared ??= load()
  return shared
}

/** The instance name if useInstance() has loaded it, without starting a load. For the tab title. */
export function loadedInstanceTitle(): string | undefined {
  return shared?.data.value?.InstanceTitle
}
