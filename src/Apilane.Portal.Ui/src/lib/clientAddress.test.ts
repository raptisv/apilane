import { describe, expect, it } from 'vitest'
import { assess, settingExamples, suggestedProxy } from './clientAddress'
import type { ClientAddressInfo, IpListState } from './clientAddress'

function info(patch: Partial<ClientAddressInfo>): ClientAddressInfo {
  return {
    ClientAddress: '203.0.113.7',
    Source: 'Connection',
    ConnectionAddress: '203.0.113.7',
    ForwardedFor: null,
    ForwardedForHonoured: false,
    Mode: 'NotConfigured',
    ConnectionIsTrustedProxy: false,
    TrustedProxies: [],
    ...patch,
  }
}

const emptyList: IpListState = { logic: 'Block', entries: [] }
const allowList: IpListState = { logic: 'Allow', entries: ['203.0.113.0/24'] }

describe('assess: TrustedProxies is not set', () => {
  it('is critical behind a proxy when the list is in use, and names the proxy to set', () => {
    const result = assess(
      info({ Source: 'ForwardedFor', ClientAddress: '203.0.113.7', ConnectionAddress: '172.18.0.2', ForwardedFor: '203.0.113.7', ForwardedForHonoured: true }),
      allowList,
    )

    expect(result.level).toBe('critical')
    expect(result.needsSetting).toBe(true)
    expect(result.steps[0]).toContain('172.18.0.2')
  })

  it('is only a warning behind a proxy while the list is empty', () => {
    const result = assess(info({ ConnectionAddress: '172.18.0.2', ForwardedFor: '203.0.113.7', ForwardedForHonoured: true }), emptyList)

    expect(result.level).toBe('warning')
  })

  it('on a direct connection says to set none, or to name the proxy', () => {
    const result = assess(info({}), allowList)

    expect(result.level).toBe('warning')
    expect(result.steps[0]).toContain('none')
    expect(result.needsSetting).toBe(true)
  })

  it('is information on a direct connection with an empty list', () => {
    expect(assess(info({}), emptyList).level).toBe('info')
  })
})

describe('assess: TrustedProxies is set', () => {
  it('works for a caller behind a trusted proxy', () => {
    const result = assess(
      info({
        Mode: 'TrustedProxies',
        TrustedProxies: ['172.18.0.0/16'],
        ConnectionAddress: '172.18.0.2',
        ConnectionIsTrustedProxy: true,
        ForwardedFor: '203.0.113.7',
        ForwardedForHonoured: true,
        Source: 'ForwardedFor',
      }),
      allowList,
    )

    expect(result.level).toBe('ok')
    expect(result.needsSetting).toBe(false)
    expect(result.steps).toEqual([])
  })

  it('warns that a trusted proxy that sends no header makes every caller look the same', () => {
    const result = assess(
      info({ Mode: 'TrustedProxies', ConnectionAddress: '172.18.0.2', ClientAddress: '172.18.0.2', ConnectionIsTrustedProxy: true }),
      allowList,
    )

    expect(result.level).toBe('warning')
    expect(result.needsSetting).toBe(false)
  })

  it('is critical when the proxy is not in the list, and says which address to add', () => {
    const result = assess(
      info({ Mode: 'TrustedProxies', TrustedProxies: ['10.0.0.0/8'], ConnectionAddress: '172.18.0.2', ClientAddress: '172.18.0.2', ForwardedFor: '203.0.113.7' }),
      allowList,
    )

    expect(result.level).toBe('critical')
    expect(result.detail).toContain('refuses all of them')
    expect(result.steps[0]).toContain('172.18.0.2')
  })

  it('works for a direct connection that is not a proxy', () => {
    expect(assess(info({ Mode: 'TrustedProxies', TrustedProxies: ['10.0.0.0/8'] }), allowList).level).toBe('ok')
  })
})

describe('assess: TrustedProxies is none', () => {
  it('works for a direct connection', () => {
    expect(assess(info({ Mode: 'NoProxies' }), allowList).level).toBe('ok')
  })

  it('is critical behind a proxy: every caller looks like the proxy', () => {
    const result = assess(info({ Mode: 'NoProxies', ConnectionAddress: '172.18.0.2', ClientAddress: '172.18.0.2', ForwardedFor: '203.0.113.7' }), allowList)

    expect(result.level).toBe('critical')
    expect(result.steps[0]).toContain('172.18.0.2')
  })
})

describe('suggestedProxy and settingExamples', () => {
  it('suggest the connection when a header came with it, and none otherwise', () => {
    expect(suggestedProxy(info({ ConnectionAddress: '172.18.0.2', ForwardedFor: '203.0.113.7' }))).toBe('172.18.0.2')
    expect(suggestedProxy(info({}))).toBe('none')
  })

  it('write the examples with the proxy of the request, or a placeholder before it is known', () => {
    expect(settingExamples(info({ ConnectionAddress: '172.18.0.2', ForwardedFor: '1.1.1.1' })).env).toBe('TrustedProxies=172.18.0.2')
    expect(settingExamples(undefined).json).toBe('"TrustedProxies": ["172.18.0.2"]')
  })
})
