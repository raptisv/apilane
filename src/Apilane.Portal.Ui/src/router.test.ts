import { afterEach, describe, expect, it, vi } from 'vitest'
import { isServerPath } from '@/lib/returnUrl'
import { afterSignIn, router } from './router'

// The tests run without a browser, so the router keeps its history in memory.
vi.mock('vue-router', async (original) => {
  const module = await original<typeof import('vue-router')>()

  return { ...module, createWebHistory: module.createMemoryHistory }
})

describe('routes', () => {
  it('opens the public screens at their addresses', () => {
    expect(router.resolve('/account/setup').name).toBe('setup')
    expect(router.resolve('/account/setup').meta.public).toBe(true)
    expect(router.resolve('/account/login').name).toBe('login')
    expect(router.resolve('/account/reset-password?code=abc').name).toBe('reset-password')
    expect(router.resolve('/account/email-confirmed').name).toBe('email-confirmed')
    expect(router.resolve('/account/login').meta.public).toBe(true)
  })

  it('matches an address whatever its letter case or trailing slash', () => {
    expect(router.resolve('/Account/Login').name).toBe('login')
    expect(router.resolve('/Account/Login?returnUrl=%2Fswagger').name).toBe('login')
    expect(router.resolve('/Admin/Servers/').name).toBe('admin-servers')
  })

  it('keeps MCP approval and connections signed-in screens available to application owners', () => {
    const approval = router.resolve('/mcp/authorize?requestId=pending-request')
    expect(approval.name).toBe('mcp-authorize')
    expect(approval.meta.public).toBeUndefined()
    expect(approval.meta.requiresAdmin).toBeUndefined()
    const connections = router.resolve('/mcp/connections')
    expect(connections.name).toBe('mcp-connections')
    expect(connections.meta.public).toBeUndefined()
    expect(connections.meta.requiresAdmin).toBeUndefined()
    expect(afterSignIn('/mcp/authorize?requestId=pending-request')).toBe('/mcp/authorize?requestId=pending-request')
  })

  it('shows Not found for any other address', () => {
    expect(router.resolve('/nope/at/all').name).toBe('not-found')
    expect(router.resolve('/nope/at/all').meta.public).toBeUndefined()
  })

  it('has no screen under an address the Portal answers itself', () => {
    const clashes = router
      .getRoutes()
      .map((route) => route.path)
      .filter(isServerPath)

    expect(clashes).toEqual([])
  })
})

describe('afterSignIn', () => {
  afterEach(() => {
    vi.unstubAllGlobals()
  })

  it('opens the applications page without a return address', () => {
    expect(afterSignIn(undefined)).toEqual({ name: 'apps' })
    expect(afterSignIn('')).toEqual({ name: 'apps' })
  })

  it('opens the applications page for an address of another site', () => {
    expect(afterSignIn('https://evil.example.com/')).toEqual({ name: 'apps' })
    expect(afterSignIn('//evil.example.com/apps')).toEqual({ name: 'apps' })
  })

  it('opens a screen of the app in the app', () => {
    expect(afterSignIn('/apps/abc/entities?x=1#top')).toBe('/apps/abc/entities?x=1#top')
    expect(afterSignIn('/')).toBe('/')
  })

  it('loads an address the Portal answers itself as a full page', () => {
    const assign = vi.fn()
    vi.stubGlobal('location', { assign })

    expect(afterSignIn('/swagger/index.html')).toBe(false)
    expect(assign).toHaveBeenCalledExactlyOnceWith('/swagger/index.html')
  })
})
