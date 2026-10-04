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

// The first segment of every address the Portal answers itself. Keep it the same as _serverPaths
// in src/Apilane.Portal/Extensions/PortalApiDependencyInjection.cs and portalPaths in vite.config.ts.
const serverPaths = ['api', 'swagger', 'health', 'metrics']

/**
 * Whether the Portal answers a same-site address itself, so this app has no screen for it. The
 * first segment decides, whatever its letter case, as on the server. Every other address is a
 * route of this app.
 *
 *   isServerPath('/swagger/index.html') -> true
 *   isServerPath('/apps?page=2')        -> false
 */
export function isServerPath(url: string): boolean {
  const first = /^\/([^/?#]*)/.exec(url)?.[1] ?? ''

  return serverPaths.includes(first.toLowerCase())
}
