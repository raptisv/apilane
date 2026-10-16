import { computed, inject, provide } from 'vue'
import type { InjectionKey } from 'vue'
import { itemSummary, propertySummary } from '@/lib/accessSummary'
import type { AccessSummary } from '@/lib/accessSummary'
import { api, unwrap } from '@/lib/api'
import { toEditable } from '@/lib/security'
import { itemKey } from '@/lib/securityAccess'
import { useApplication } from './useApplication'
import { useAsync } from './useAsync'

/** What a list row or a property row asks for: who may call it. Undefined while the rules are not known. */
export interface AccessLookup {
  /** An entity, a custom endpoint or the schema: `item('Entity', 'Orders')`. */
  item: (type: string, name: string) => AccessSummary | undefined
  /** A property of an entity. */
  property: (entityName: string, propertyName: string) => AccessSummary | undefined
}

const nothing: AccessLookup = { item: () => undefined, property: () => undefined }

const key: InjectionKey<AccessLookup> = Symbol('access')

/**
 * Loads the security rules of the application once and gives them to the rows below the screen:
 *
 *   provideAccessSummaries()           // in the screen
 *   const access = useAccessSummaries() // in a row
 *   access.item('Entity', entity.Name)
 *
 * The answer is a hint, not the screen's content: if the rules cannot be read the rows simply show no
 * badge, and nothing else on the screen changes.
 */
export function provideAccessSummaries(): AccessLookup {
  const { application } = useApplication()
  const appToken = application.value.Token

  const { data } = useAsync(() => unwrap(api.GET('/api/v1/applications/{appToken}/security', { params: { path: { appToken } } })))

  const rules = computed(() => data.value?.Rules.map(toEditable) ?? [])
  const items = computed(() => new Map((data.value?.Items ?? []).map((item) => [itemKey(item), item])))

  const lookup: AccessLookup = {
    item: (type, name) => {
      const item = items.value.get(`${type}-${name}`)

      return item && data.value ? itemSummary(item, rules.value, data.value.Roles) : undefined
    },
    property: (entityName, propertyName) => {
      const item = items.value.get(`Entity-${entityName}`)

      return item && data.value ? propertySummary(item, propertyName, rules.value, data.value.Roles) : undefined
    },
  }

  provide(key, lookup)

  return lookup
}

/** The lookup of the screen above, or one that knows nothing when no screen provides it. */
export function useAccessSummaries(): AccessLookup {
  return inject(key, nothing)
}
