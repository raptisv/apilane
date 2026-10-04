import { computed, ref, watch } from 'vue'
import { api, unwrap } from '@/lib/api'
import { useAsync } from './useAsync'

/**
 * The API servers an application can be put on, and the one the user picked. Use it in a form
 * that creates an application (new, import, clone), with ServerSelect and NoServersState:
 *
 *   const { servers, serverId, error, loading, reload } = useServerChoice()
 *
 * `serverId` starts undefined; when the instance has exactly one server, that one is picked.
 */
export function useServerChoice() {
  const { data, error, loading, reload } = useAsync(() => unwrap(api.GET('/api/v1/servers')))

  const servers = computed(() => data.value?.Data ?? [])
  const serverId = ref<number>()

  watch(servers, (list) => {
    if (list.length === 1 && list[0]) {
      serverId.value = list[0].ID
    }
  })

  return { servers, serverId, error, loading, reload }
}
