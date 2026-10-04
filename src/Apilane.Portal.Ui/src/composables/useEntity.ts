import { inject, provide, shallowRef, watch } from 'vue'
import type { InjectionKey, ShallowRef } from 'vue'
import { api, unwrap } from '@/lib/api'
import type { Entity } from '@/lib/entities'
import { useAsync } from './useAsync'

interface EntityContext {
  entity: Readonly<ShallowRef<Entity>>
  reload: () => Promise<void>
}

const key: InjectionKey<EntityContext> = Symbol('entity')

/**
 * The entity of the screen, for every screen under /apps/:appToken/entities/:entity. EntityLayout
 * has loaded it before the screen is shown, so it is never undefined:
 *
 *   const { entity, reload } = useEntity()
 *
 * Call `reload()` after a write that changes what a screen under EntityLayout reads from the
 * entity (its properties, its description).
 */
export function useEntity(): EntityContext {
  const context = inject(key)

  if (!context) {
    throw new Error('useEntity() works only in a screen under EntityLayout (a child of the entities/:entity route).')
  }

  return context
}

/**
 * Loads the entity and hands it to the screens below. Only EntityLayout calls this. It returns
 * the states of the load; EntityLayout shows the screen only while `data` is set.
 */
export function provideEntity(appToken: string, entityName: () => string) {
  const state = useAsync(
    () =>
      unwrap(
        api.GET('/api/v1/applications/{appToken}/entities/{entity}', {
          params: { path: { appToken, entity: entityName() } },
        }),
      ),
    { watch: entityName },
  )

  // What the screens read. It keeps the last loaded entity, so a screen that is on its way out
  // (a failed reload, another entity) never reads undefined.
  const entity = shallowRef<Entity>()

  watch(state.data, (loaded) => {
    if (loaded) {
      entity.value = loaded
    }
  })

  provide(key, { entity: entity as ShallowRef<Entity>, reload: state.reload })

  return state
}
