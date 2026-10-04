/**
 * Saves a Blob as a file on the user's device, under `fileName`. Use it for a file that was
 * fetched with headers (see lib/apiServer.ts), where a plain link cannot be used because the
 * token must not go into an address.
 *
 *   saveBlob(await apiServer(url, token).getBlob('/api/Application/Export'), `${token}.zip`)
 */
export function saveBlob(blob: Blob, fileName: string): void {
  const url = URL.createObjectURL(blob)
  const link = document.createElement('a')

  link.href = url
  link.download = fileName
  document.body.append(link)
  link.click()
  link.remove()

  // Not at once: some browsers start the download a moment after the click.
  setTimeout(() => URL.revokeObjectURL(url), 1000)
}
