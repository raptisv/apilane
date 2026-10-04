/**
 * Returns `value` when it is safe to follow after signing in, and undefined otherwise.
 *
 * `returnUrl` comes from the address bar, so anyone can put anything into a link to the login
 * page. Only a path on this site is followed: it starts with one '/'. Everything a browser could
 * read as another site is refused: a full address ('https://...'), '//host', and the forms with a
 * backslash or a control character, which browsers turn into '//host' ('/\host', '/<tab>/host').
 */
export function safeReturnUrl(value: unknown): string | undefined {
  if (typeof value !== 'string' || !value.startsWith('/') || value.startsWith('//')) {
    return undefined
  }

  if (/[\\\u0000-\u001f\u007f]/.test(value)) {
    return undefined
  }

  // Last check, by the browser's own rules: resolved against a made-up site, the address must stay on it.
  try {
    return new URL(value, 'http://site.invalid').origin === 'http://site.invalid' ? value : undefined
  } catch {
    return undefined
  }
}

/**
 * The route path inside this app of a same-site address, or undefined when the address belongs
 * to the classic portal. `base` is the folder the app is served from, '/ui/'.
 *
 *   appPath('/ui/admin/users?page=2', '/ui/') -> '/admin/users?page=2'
 *   appPath('/Application/Entities', '/ui/')  -> undefined
 */
export function appPath(url: string, base: string): string | undefined {
  const root = base.replace(/\/$/, '')

  if (url === root) {
    return '/'
  }

  return url.startsWith(`${root}/`) || url.startsWith(`${root}?`) || url.startsWith(`${root}#`)
    ? url.slice(root.length)
    : undefined
}
