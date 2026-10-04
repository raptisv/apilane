import { describe, expect, it } from 'vitest'
import { appPath, safeReturnUrl } from './returnUrl'

describe('safeReturnUrl', () => {
  it('accepts a path on this site, with query and hash', () => {
    expect(safeReturnUrl('/')).toBe('/')
    expect(safeReturnUrl('/ui/admin/users?page=2#top')).toBe('/ui/admin/users?page=2#top')
    expect(safeReturnUrl('/App/abc/Application/Entities')).toBe('/App/abc/Application/Entities')
  })

  it('refuses anything that is not a string', () => {
    expect(safeReturnUrl(undefined)).toBeUndefined()
    expect(safeReturnUrl(null)).toBeUndefined()
    expect(safeReturnUrl(['/ui/'])).toBeUndefined()
    expect(safeReturnUrl(42)).toBeUndefined()
  })

  it('refuses an empty value and a relative path', () => {
    expect(safeReturnUrl('')).toBeUndefined()
    expect(safeReturnUrl('ui/admin')).toBeUndefined()
  })

  it('refuses a full address', () => {
    expect(safeReturnUrl('https://evil.example.com/')).toBeUndefined()
    expect(safeReturnUrl('javascript:alert(1)')).toBeUndefined()
  })

  it("refuses '//host'", () => {
    expect(safeReturnUrl('//evil.example.com')).toBeUndefined()
    expect(safeReturnUrl('//evil.example.com/ui/')).toBeUndefined()
  })

  it('refuses a backslash, which browsers read as a slash', () => {
    expect(safeReturnUrl('/\\evil.example.com')).toBeUndefined()
    expect(safeReturnUrl('/ui\\admin')).toBeUndefined()
  })

  it('refuses control characters, which browsers drop', () => {
    expect(safeReturnUrl('/\t/evil.example.com')).toBeUndefined()
    expect(safeReturnUrl('/\n/evil.example.com')).toBeUndefined()
    expect(safeReturnUrl('/ui/\u0000')).toBeUndefined()
    expect(safeReturnUrl('/ui/\u007f')).toBeUndefined()
  })
})

describe('appPath', () => {
  it('returns the route path of an address inside the app', () => {
    expect(appPath('/ui/admin/users?page=2', '/ui/')).toBe('/admin/users?page=2')
    expect(appPath('/ui/', '/ui/')).toBe('/')
  })

  it("returns '/' for the app folder without a trailing slash", () => {
    expect(appPath('/ui', '/ui/')).toBe('/')
  })

  it('keeps a query or hash that follows the folder name directly', () => {
    expect(appPath('/ui?x=1', '/ui/')).toBe('?x=1')
    expect(appPath('/ui#top', '/ui/')).toBe('#top')
  })

  it('returns undefined for an address of the classic portal', () => {
    expect(appPath('/Application/Entities', '/ui/')).toBeUndefined()
    expect(appPath('/', '/ui/')).toBeUndefined()
  })

  it('does not take a folder that only starts with the same letters', () => {
    expect(appPath('/uix/admin', '/ui/')).toBeUndefined()
  })
})
