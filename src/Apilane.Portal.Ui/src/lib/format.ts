/**
 * Date formats shared by every screen. The API sends dates as UTC ISO strings.
 *
 * - formatDateTime: the user's local time, for ordinary dates. Put the UTC value in a tooltip:
 *
 *     <time :datetime="user.LastLogin" :title="`${formatUtc(user.LastLogin)} UTC`">{{ formatDateTime(user.LastLogin) }}</time>
 *
 * - formatUtc: 'yyyy-MM-dd HH:mm:ss' in UTC, for logs, where entries are compared with server logs.
 */
const local = new Intl.DateTimeFormat(undefined, { dateStyle: 'medium', timeStyle: 'short' })

export function formatDateTime(value: string): string {
  const date = new Date(value)

  return Number.isNaN(date.getTime()) ? value : local.format(date)
}

export function formatUtc(value: string): string {
  const date = new Date(value)

  return Number.isNaN(date.getTime()) ? value : date.toISOString().slice(0, 19).replace('T', ' ')
}
