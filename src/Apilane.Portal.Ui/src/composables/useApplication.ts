import { inject, provide, shallowRef, watch } from 'vue'
import type { InjectionKey, ShallowRef } from 'vue'
import { api, unwrap } from '@/lib/api'
import type { Application } from '@/lib/applications'
import { useAsync } from './useAsync'

interface ApplicationContext {
  application: Readonly<ShallowRef<Application>>
  reload: () => Promise<void>
}

const key: InjectionKey<ApplicationContext> = Symbol('application')

/**
 * The application of the screen, for every screen under /apps/:appToken. AppLayout has loaded it
 * before the screen is shown, so it is never undefined:
 *
 *   const { application, reload } = useApplication()
 *
 * Call `reload()` after a write that changes what the application answer carries (its name, its
 * status, the number of custom endpoints), so the breadcrumb and the other screens follow.
 */
export function useApplication(): ApplicationContext {
  const context = inject(key)

  if (!context) {
    throw new Error('useApplication() works only in a screen under AppLayout (a child of the apps/:appToken route).')
  }

  return context
}

/**
 * Loads the application and hands it to the screens below. Only AppLayout calls this. It returns
 * the states of the load; AppLayout shows the screen only while `data` is set.
 */
export function provideApplication(appToken: () => string) {
  const state = useAsync(() => unwrap(api.GET('/api/v1/applications/{appToken}', { params: { path: { appToken: appToken() } } })), {
    watch: appToken,
  })

  // What the screens read. It keeps the last loaded application, so a screen that is on its way
  // out (a failed reload, another application) never reads undefined.
  const application = shallowRef<Application>()

  watch(state.data, (loaded) => {
    if (loaded) {
      application.value = loaded
    }
  })

  provide(key, { application: application as ShallowRef<Application>, reload: state.reload })

  return state
}
