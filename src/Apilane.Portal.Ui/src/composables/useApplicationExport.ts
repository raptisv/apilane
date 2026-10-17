import { apiServer } from '@/lib/apiServer'
import type { Application } from '@/lib/applications'
import { saveBlob } from '@/lib/download'
import * as toast from '@/lib/toast'
import { useMutation } from './useMutation'

/**
 * Downloads the export of an application: the ZIP its API server builds, saved as <token>.zip.
 * For the card menu of the applications page and the settings screen alike.
 *
 *   const exportZip = useApplicationExport(() => props.application)
 *
 *   <Button :disabled="exportZip.pending.value" @click="exportZip.run">Export</Button>
 *
 * `run()` does nothing while an export is running, and shows a failure as an error toast.
 */
export function useApplicationExport(application: () => Pick<Application, 'Token' | 'Server'>) {
  const download = useMutation(async () => {
    const app = application()

    saveBlob(await apiServer(app.Server.ServerUrl, app.Token).getBlob('/api/Application/Export'), `${app.Token}.zip`)
  })

  async function run(): Promise<void> {
    if (download.pending.value) {
      return
    }

    if (!(await download.run())) {
      toast.error(download.error.value)
    }
  }

  return { pending: download.pending, run }
}
