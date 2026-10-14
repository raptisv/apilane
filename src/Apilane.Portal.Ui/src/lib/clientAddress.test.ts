import { describe, expect, it } from 'vitest'
import { addressesBeyond, assess, settingExamples, suggestedProxy } from './clientAddress'
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

describe('addressesBeyond', () => {
  const used = { Mode: 'TrustedProxies', ForwardedForHonoured: true, ConnectionIsTrustedProxy: true, ClientAddress: '172.70.1.2' }

  it('lists what stands to the left of the address in use', () => {
    expect(addressesBeyond(info({ ...used, ForwardedFor: '6.6.6.6, 203.0.113.7, 172.70.1.2' }))).toEqual(['6.6.6.6', '203.0.113.7'])
  })

  it('is empty when the address in use is the first of the header', () => {
    expect(addressesBeyond(info({ ...used, ClientAddress: '203.0.113.7', ForwardedFor: '203.0.113.7, 172.18.0.2' }))).toEqual([])
  })

  it('is empty when the header did not decide the address', () => {
    expect(addressesBeyond(info({ ...used, ForwardedForHonoured: false, ForwardedFor: '203.0.113.7, 172.70.1.2' }))).toEqual([])
    expect(addressesBeyond(info({ ...used, ForwardedFor: null }))).toEqual([])
  })

  it('reads an address with a port', () => {
    expect(addressesBeyond(info({ ...used, ClientAddress: '172.70.1.2', ForwardedFor: '203.0.113.7:5000, 172.70.1.2:80' }))).toEqual(['203.0.113.7'])
  })
})

describe('assess: more addresses in the header than the one in use', () => {
  const behindTwoProxies = {
    Mode: 'TrustedProxies',
    TrustedProxies: ['172.30.0.1'],
    ConnectionAddress: '172.30.0.1',
    ConnectionIsTrustedProxy: true,
    ForwardedForHonoured: true,
    Source: 'ForwardedFor',
    ClientAddress: '172.70.1.2',
    ForwardedFor: '203.0.113.7, 172.70.1.2',
  }

  it('asks the owner to check, names the address to the left and what to add', () => {
    const result = assess(info(behindTwoProxies), allowList)

    expect(result.level).toBe('warning')
    expect(result.title).toContain('Check the address in use')
    expect(result.detail).toContain('203.0.113.7')
    expect(result.steps[0]).toContain('172.70.1.2')
    expect(result.steps[0]).toContain('203.0.113.7')
    expect(result.needsSetting).toBe(true)
  })

  it('is only information while the list is empty', () => {
    expect(assess(info(behindTwoProxies), emptyList).level).toBe('info')
  })

  it('works when the caller is the first of the header', () => {
    const result = assess(info({ ...behindTwoProxies, ClientAddress: '203.0.113.7', TrustedProxies: ['172.30.0.1', '172.64.0.0/13'] }), allowList)

    expect(result.level).toBe('ok')
  })

  it('tells an untrusted proxy to list the other proxies of the chain too', () => {
    const result = assess(
      info({ Mode: 'TrustedProxies', TrustedProxies: ['10.0.0.0/8'], ConnectionAddress: '172.30.0.1', ClientAddress: '172.30.0.1', ForwardedFor: '203.0.113.7, 172.70.1.2' }),
      allowList,
    )

    expect(result.level).toBe('critical')
    expect(result.steps[0]).toContain('172.30.0.1')
    expect(result.steps.join(' ')).toContain('other proxies')
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
