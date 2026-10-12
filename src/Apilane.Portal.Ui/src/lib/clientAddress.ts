// How an API server finds the address of a caller, for the IP access card of the security screen.
//
// The IP list of an application (Block or Allow) is looked up with "the address of the caller". The
// API server takes it from the connection, or from the X-Forwarded-For header that a reverse proxy
// adds. The header can be written by anyone who reaches the server, so the server believes it only
// as far as its TrustedProxies setting says. The screen reads what the server sees for the browser's
// own request (GET /api/Application/GetClientAddress, through lib/apiServer.ts) and says what, if
// anything, has to be changed on the server.

/** The answer of GET /api/Application/GetClientAddress. */
export interface ClientAddressInfo {
  /** The address the IP list is looked up with, for this request. */
  ClientAddress: string
  /** 'Connection', 'ForwardedFor', 'RemoteAddrHeader' or 'None'. */
  Source: string
  /** The address of the connection that reached the API server: the proxy, when there is one. */
  ConnectionAddress: string
  /** The X-Forwarded-For header as it arrived, or null when there was none. */
  ForwardedFor: string | null
  /** True when the header decided the address. */
  ForwardedForHonoured: boolean
  /** 'NotConfigured' (the setting is not set), 'TrustedProxies' or 'NoProxies' (the setting is "none"). */
  Mode: string
  /** True when the connection comes from one of the TrustedProxies. */
  ConnectionIsTrustedProxy: boolean
  /** The proxies of the setting, as the server stores them. */
  TrustedProxies: string[]
}

/** What the IP list of the application is, as saved. */
export interface IpListState {
  /** 'Block' or 'Allow'. */
  logic: string
  /** The saved entries. */
  entries: readonly string[]
}

export type Level = 'ok' | 'info' | 'warning' | 'critical'

/** What the screen tells the owner about the address of callers. */
export interface Assessment {
  level: Level
  /** One line: the state. */
  title: string
  /** What it means, in a sentence or two. */
  detail: string
  /** What to do, in order. Empty when nothing has to be done. */
  steps: string[]
  /** Whether the TrustedProxies setting has to be changed on the server. */
  needsSetting: boolean
}

/** True when the list can refuse somebody: a non-empty list in either logic. */
function listInUse(list: IpListState): boolean {
  return list.entries.some((entry) => entry.trim() !== '')
}

/** The value to put in the TrustedProxies setting for what this request showed. */
export function suggestedProxy(info: ClientAddressInfo): string {
  return info.ForwardedFor !== null && info.ConnectionAddress !== '' ? info.ConnectionAddress : 'none'
}

const RESTART = 'Restart the API server: the setting is read at start.'
const RELOAD = 'Reload this screen: it then shows "Works" when the address is the one of the caller.'

/**
 * What the owner has to know and do, from what the server saw for this request and the list of the
 * application. Pure: the same inputs give the same words.
 */
export function assess(info: ClientAddressInfo, list: IpListState): Assessment {
  const inUse = listInUse(list)
  const header = info.ForwardedFor !== null
  const proxy = info.ConnectionAddress

  if (info.Mode === 'NotConfigured') {
    if (header) {
      return {
        level: inUse ? 'critical' : 'warning',
        title: 'The API server believes any X-Forwarded-For header',
        detail:
          `This request came through ${proxy || 'a proxy'} and the API server used ${info.ClientAddress}, the first entry of the header. ` +
          'Anyone who can reach the API server can send that header with any address, so the IP list can be bypassed ' +
          '(in Allow mode by writing an address of the list, in Block mode by writing another one).',
        steps: [
          `Set TrustedProxies on the API server to ${proxy || 'the address of your reverse proxy'}: the proxy, not the caller. Use a network (172.18.0.0/16) when its address changes.`,
          'Make sure the proxy sets or appends X-Forwarded-For (see the examples).',
          RESTART,
          RELOAD,
        ],
        needsSetting: true,
      }
    }

    return {
      level: inUse ? 'warning' : 'info',
      title: 'TrustedProxies is not set',
      detail:
        `This request reached the API server directly, as ${info.ClientAddress}. A caller can still send an X-Forwarded-For header, ` +
        'and while the setting is not set the server believes it. The IP list can be bypassed that way.',
      steps: [
        'If callers reach the API server directly: set TrustedProxies to none, so that the header is ignored.',
        'If a reverse proxy sits in front of it: make the proxy add X-Forwarded-For, then set TrustedProxies to the proxy.',
        RESTART,
        RELOAD,
      ],
      needsSetting: true,
    }
  }

  if (info.Mode === 'NoProxies') {
    if (header) {
      return {
        level: inUse ? 'critical' : 'warning',
        title: 'The header is ignored, so every caller looks like the proxy',
        detail:
          `TrustedProxies is none, and this request came through ${proxy || 'a proxy'} with an X-Forwarded-For header. ` +
          `The IP list sees ${info.ClientAddress} for every caller${inUse && list.logic === 'Allow' ? ': an Allow list then refuses everyone but that address' : ''}.`,
        steps: [`Replace none by ${proxy || 'the address of your reverse proxy'} in TrustedProxies.`, RESTART, RELOAD],
        needsSetting: true,
      }
    }

    return {
      level: 'ok',
      title: 'Works: the address is the one of the connection',
      detail: `The API server ignores X-Forwarded-For (TrustedProxies is none) and used ${info.ClientAddress}, the address of the connection. This is right when callers reach the API server directly.`,
      steps: [],
      needsSetting: false,
    }
  }

  // TrustedProxies
  if (info.ConnectionIsTrustedProxy && info.ForwardedForHonoured) {
    return {
      level: 'ok',
      title: 'Works: the address comes from a trusted proxy',
      detail:
        `The request came through ${proxy}, which is in TrustedProxies, and the API server used ${info.ClientAddress}, ` +
        'the address that proxy saw. A header written by a caller before it reached the proxy is not believed.',
      steps: [],
      needsSetting: false,
    }
  }

  if (info.ConnectionIsTrustedProxy) {
    return {
      level: inUse ? 'warning' : 'info',
      title: 'The proxy sent no X-Forwarded-For',
      detail:
        `${proxy} is a trusted proxy, but this request had no X-Forwarded-For header (or only addresses of trusted proxies), ` +
        `so the IP list sees ${info.ClientAddress}. Every caller of that proxy would look the same.`,
      steps: ['Make the proxy set or append X-Forwarded-For (see the examples).', RELOAD],
      needsSetting: false,
    }
  }

  if (header) {
    return {
      level: inUse ? 'critical' : 'warning',
      title: 'The header is ignored: the connection is not a trusted proxy',
      detail:
        `This request came from ${proxy || 'an address'} with an X-Forwarded-For header, but that address is not in TrustedProxies, ` +
        `so the header is ignored and the IP list sees ${info.ClientAddress} for every caller that comes this way` +
        `${inUse && list.logic === 'Allow' ? ' (an Allow list then refuses all of them)' : ''}.`,
      steps: [`Add ${proxy || 'the address of your reverse proxy'} to TrustedProxies. A network works too.`, RESTART, RELOAD],
      needsSetting: true,
    }
  }

  return {
    level: 'ok',
    title: 'Works: a direct connection',
    detail:
      `This request came straight from ${info.ClientAddress}, which is not a trusted proxy, so the IP list is looked up with it. ` +
      'A header sent along would be ignored. If a reverse proxy is in front of the API server, add it to TrustedProxies.',
    steps: [],
    needsSetting: false,
  }
}

/** The settings of the examples, for the proxy this request came through (or a placeholder). */
export function settingExamples(info: ClientAddressInfo | undefined): { json: string; env: string; compose: string } {
  const value = info !== undefined && info.ConnectionAddress !== '' && info.ForwardedFor !== null ? info.ConnectionAddress : '172.18.0.2'

  return {
    json: `"TrustedProxies": ["${value}"]`,
    env: `TrustedProxies=${value}`,
    compose: `services:\n  apilane-api:\n    environment:\n      - TrustedProxies=${value}`,
  }
}
