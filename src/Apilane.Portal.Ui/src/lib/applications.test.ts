import { describe, expect, it, vi } from 'vitest'
import { router } from '@/router'
import {
  applicationSections,
  databaseTypeLabel,
  databaseTypes,
  filterByName,
  formatStorage,
  groupByServer,
  needsConnectionString,
  sectionOf,
  switcherTarget,
} from './applications'
import type { Application, ApplicationServer } from './applications'

// The sections are checked against the route table, which keeps its history in memory without a browser.
vi.mock('vue-router', async (original) => {
  const module = await original<typeof import('vue-router')>()

  return { ...module, createWebHistory: module.createMemoryHistory }
})

const alpha: ApplicationServer = { ID: 2, Name: 'Alpha', ServerUrl: 'https://alpha.example.com' }
const beta: ApplicationServer = { ID: 1, Name: 'beta', ServerUrl: 'https://beta.example.com' }

function app(name: string, server: ApplicationServer, isOwner = true): Application {
  return {
    Token: `token-${name}`,
    Name: name,
    Online: true,
    DatabaseType: 'SQLLite',
    HasConnectionString: false,
    DifferentiationEntity: null,
    MaxAllowedFileSizeInKB: 100,
    IsOwner: isOwner,
    OwnerEmail: 'owner@example.com',
    CollaboratorCount: 0,
    CustomEndpointCount: 0,
    Server: server,
  }
}

function names(applications: Application[]): string[] {
  return applications.map((application) => application.Name)
}

describe('groupByServer', () => {
  it('returns no groups for no applications', () => {
    expect(groupByServer([])).toEqual([])
  })

  it('makes one group per server, ordered by server name ignoring case', () => {
    const groups = groupByServer([app('one', beta), app('two', alpha), app('three', beta)])

    expect(groups.map((group) => group.server.Name)).toEqual(['Alpha', 'beta'])
    expect(names(groups[0].applications)).toEqual(['two'])
    expect(names(groups[1].applications)).toEqual(['one', 'three'])
  })

  it('puts owned applications first, then shared ones, each by name ignoring case', () => {
    const groups = groupByServer([
      app('zebra', alpha, false),
      app('Mango', alpha),
      app('apple', alpha, false),
      app('banana', alpha),
    ])

    expect(names(groups[0].applications)).toEqual(['banana', 'Mango', 'apple', 'zebra'])
  })

  it('keeps servers with the same name apart, ordered by ID', () => {
    const first: ApplicationServer = { ID: 5, Name: 'Same', ServerUrl: 'https://a.example.com' }
    const second: ApplicationServer = { ID: 3, Name: 'Same', ServerUrl: 'https://b.example.com' }

    const groups = groupByServer([app('one', first), app('two', second)])

    expect(groups.map((group) => group.server.ID)).toEqual([3, 5])
  })

  it('does not change the list it was given', () => {
    const list = [app('b', alpha), app('a', alpha)]

    groupByServer(list)

    expect(names(list)).toEqual(['b', 'a'])
  })
})

describe('filterByName', () => {
  const list = [app('Shop', alpha), app('Workshop API', alpha), app('Blog', beta)]

  it('keeps every application for an empty or blank search', () => {
    expect(filterByName(list, '')).toHaveLength(3)
    expect(filterByName(list, '   ')).toHaveLength(3)
  })

  it('matches a part of the name, ignoring case and surrounding spaces', () => {
    expect(names(filterByName(list, ' SHOP '))).toEqual(['Shop', 'Workshop API'])
  })

  it('returns nothing when no name matches', () => {
    expect(filterByName(list, 'crm')).toEqual([])
  })
})

describe('formatStorage', () => {
  it('shows a value below 1024 MB in MB with two decimals', () => {
    expect(formatStorage(0)).toBe('0.00 MB')
    expect(formatStorage(12.345)).toBe('12.35 MB')
    expect(formatStorage(1023.99)).toBe('1023.99 MB')
  })

  it('shows a value from 1024 MB up in GB with two decimals', () => {
    expect(formatStorage(1024)).toBe('1.00 GB')
    expect(formatStorage(1536)).toBe('1.50 GB')
  })
})

describe('databaseTypeLabel', () => {
  it('gives the display name of each database type', () => {
    expect(databaseTypeLabel('SQLLite')).toBe('SQLite')
    expect(databaseTypeLabel('SQLServer')).toBe('SQL Server')
    expect(databaseTypeLabel('MySQL')).toBe('MySQL')
    expect(databaseTypeLabel('PostgreSQL')).toBe('PostgreSQL')
  })

  it('shows an unknown value as it is', () => {
    expect(databaseTypeLabel('Oracle')).toBe('Oracle')
  })
})

describe('needsConnectionString', () => {
  it('is false for SQLite only', () => {
    expect(databaseTypes.filter((type) => !needsConnectionString(type))).toEqual(['SQLLite'])
  })
})

describe('applicationSections', () => {
  it('names the route of each section, which opens at /apps/<token>/<segment>', () => {
    for (const section of applicationSections) {
      const target = router.resolve({ name: section.name, params: { appToken: 'A1' } })

      expect(target.path).toBe(`/apps/A1/${section.segment}`)
      expect(target.meta.requiresOwner === true).toBe(section.ownerOnly === true)
    }
  })
})

describe('sectionOf', () => {
  it('finds the section of an address and of the screens below it, whatever the letter case', () => {
    expect(sectionOf('/apps/A1/security')?.name).toBe('app-security')
    expect(sectionOf('/apps/A1/entities/Orders/constraints')?.name).toBe('app-entities')
    expect(sectionOf('/apps/A1/endpoints/12')?.name).toBe('app-endpoints')
    expect(sectionOf('/apps/A1/reports/3/edit')?.name).toBe('app-reports')
    expect(sectionOf('/apps/A1/import')?.name).toBe('app-schema-import')
    expect(sectionOf('/Apps/A1/Audit-Log')?.name).toBe('app-audit-log')
  })

  it('finds none outside the sections of an application', () => {
    expect(sectionOf('/apps/A1/clone')).toBeUndefined()
    expect(sectionOf('/apps/A1')).toBeUndefined()
    expect(sectionOf('/apps')).toBeUndefined()
    expect(sectionOf('/apps/import')).toBeUndefined()
    expect(sectionOf('/admin/applications/A1/data')).toBeUndefined()
  })
})

describe('switcherTarget', () => {
  const owned = { Token: 'B2', IsOwner: true }
  const shared = { Token: 'B2', IsOwner: false }

  it('opens the section the user is on, with nothing of the application being left', () => {
    expect(switcherTarget('/apps/A1/security', owned)).toEqual({ name: 'app-security', params: { appToken: 'B2' } })
    expect(switcherTarget('/apps/A1/data/Orders', owned)).toEqual({ name: 'app-data', params: { appToken: 'B2' } })
    expect(switcherTarget('/apps/A1/entities/Orders/sorting', owned)).toEqual({ name: 'app-entities', params: { appToken: 'B2' } })
    expect(switcherTarget('/apps/A1/endpoints/12', owned)).toEqual({ name: 'app-endpoints', params: { appToken: 'B2' } })
    expect(switcherTarget('/apps/A1/reports/new', owned)).toEqual({ name: 'app-reports', params: { appToken: 'B2' } })
    expect(switcherTarget('/apps/A1/import', shared)).toEqual({ name: 'app-schema-import', params: { appToken: 'B2' } })
  })

  it('opens Sharing only for an application the user owns', () => {
    expect(switcherTarget('/apps/A1/sharing', owned).name).toBe('app-sharing')
    expect(switcherTarget('/apps/A1/sharing', shared).name).toBe('app-entities')
  })

  it('opens Entities from an address in no section', () => {
    expect(switcherTarget('/apps/A1/clone/abc', owned).name).toBe('app-entities')
    expect(switcherTarget('/apps', owned).name).toBe('app-entities')
    expect(switcherTarget('/admin/applications/A1/data/Orders', owned).name).toBe('app-entities')
    expect(switcherTarget('/mcp/connections', owned).name).toBe('app-entities')
  })
})
