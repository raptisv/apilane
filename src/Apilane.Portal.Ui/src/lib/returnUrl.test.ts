import { describe, expect, it } from 'vitest'
import { isServerPath, safeReturnUrl } from './returnUrl'

describe('safeReturnUrl', () => {
  it('accepts a path on this site, with query and hash', () => {
    expect(safeReturnUrl('/')).toBe('/')
    expect(safeReturnUrl('/admin/users?page=2#top')).toBe('/admin/users?page=2#top')
    expect(safeReturnUrl('/apps/abc/entities')).toBe('/apps/abc/entities')
    expect(safeReturnUrl('/swagger/index.html')).toBe('/swagger/index.html')
  })

  it('refuses anything that is not a string', () => {
    expect(safeReturnUrl(undefined)).toBeUndefined()
    expect(safeReturnUrl(null)).toBeUndefined()
    expect(safeReturnUrl(['/apps'])).toBeUndefined()
    expect(safeReturnUrl(42)).toBeUndefined()
  })

  it('refuses an empty value and a relative path', () => {
    expect(safeReturnUrl('')).toBeUndefined()
    expect(safeReturnUrl('admin/users')).toBeUndefined()
  })

  it('refuses a full address', () => {
    expect(safeReturnUrl('https://evil.example.com/')).toBeUndefined()
    expect(safeReturnUrl('javascript:alert(1)')).toBeUndefined()
  })

  it("refuses '//host'", () => {
    expect(safeReturnUrl('//evil.example.com')).toBeUndefined()
    expect(safeReturnUrl('//evil.example.com/apps')).toBeUndefined()
  })

  it('refuses a backslash, which browsers read as a slash', () => {
    expect(safeReturnUrl('/\\evil.example.com')).toBeUndefined()
    expect(safeReturnUrl('/admin\\users')).toBeUndefined()
  })

  it('refuses control characters, which browsers drop', () => {
    expect(safeReturnUrl('/\t/evil.example.com')).toBeUndefined()
    expect(safeReturnUrl('/\n/evil.example.com')).toBeUndefined()
    expect(safeReturnUrl('/apps/\u0000')).toBeUndefined()
    expect(safeReturnUrl('/apps/\u007f')).toBeUndefined()
  })
})

describe('isServerPath', () => {
  it('is true for an address the Portal answers itself', () => {
    expect(isServerPath('/swagger')).toBe(true)
    expect(isServerPath('/swagger/')).toBe(true)
    expect(isServerPath('/swagger/index.html')).toBe(true)
    expect(isServerPath('/api/v1/session')).toBe(true)
    expect(isServerPath('/api/internal/applications/abc')).toBe(true)
    expect(isServerPath('/health/liveness')).toBe(true)
    expect(isServerPath('/metrics')).toBe(true)
  })

  it('looks at the first segment only, not at the query or the hash', () => {
    expect(isServerPath('/swagger?x=1')).toBe(true)
    expect(isServerPath('/swagger#top')).toBe(true)
    expect(isServerPath('/apps?returnUrl=/swagger')).toBe(false)
    expect(isServerPath('/apps/swagger')).toBe(false)
    expect(isServerPath('/apps#/api')).toBe(false)
  })

  it('ignores letter case, as the server does', () => {
    expect(isServerPath('/Swagger/index.html')).toBe(true)
    expect(isServerPath('/API/v1/session')).toBe(true)
  })

  it('is false for a screen of this app', () => {
    expect(isServerPath('/')).toBe(false)
    expect(isServerPath('/apps')).toBe(false)
    expect(isServerPath('/apps/abc/entities')).toBe(false)
    expect(isServerPath('/admin/servers')).toBe(false)
    expect(isServerPath('/account/login')).toBe(false)
  })

  it('does not take a segment that only starts with the same letters', () => {
    expect(isServerPath('/apiary')).toBe(false)
    expect(isServerPath('/swagger-ui')).toBe(false)
    expect(isServerPath('/healthy')).toBe(false)
  })
})
