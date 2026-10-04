/**
 * CSV text for a table, for an export the user opens in a spreadsheet. Every field is quoted and a
 * double quote inside it is doubled (RFC 4180), so commas, quotes and line breaks in a value stay
 * inside its cell. A null is an empty cell. Rows end with CRLF. Pure functions (csv.test.ts).
 *
 *   saveBlob(new Blob([toCsv(headers, rows)], { type: 'text/csv;charset=utf-8' }), csvFileName('Orders', new Date()))
 */
export function toCsv(headers: readonly string[], rows: readonly (readonly (string | null)[])[]): string {
  return [headers, ...rows].map((row) => row.map(csvField).join(',')).join('\r\n')
}

function csvField(value: string | null): string {
  return `"${(value ?? '').replaceAll('"', '""')}"`
}

/** The file name of an export: 'Orders_2026_10_03_14_05_09.csv', in local time. */
export function csvFileName(name: string, now: Date): string {
  const pad = (n: number) => String(n).padStart(2, '0')
  const stamp = [now.getFullYear(), pad(now.getMonth() + 1), pad(now.getDate()), pad(now.getHours()), pad(now.getMinutes()), pad(now.getSeconds())]

  return `${name}_${stamp.join('_')}.csv`
}
