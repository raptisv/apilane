import { describe, expect, it } from 'vitest'
import { applicationDetails, countText, searchApplications } from './adminApplications'
import type { AdminApplication } from './adminApplications'

function app(name: string, token: string, ownerEmail: string | null): AdminApplication {
  return {
    ID: 1,
    Token: token,
    Name: name,
    OwnerEmail: ownerEmail,
    Online: true,
    Server: { ID: 1, Name: 'Main', ServerUrl: 'https://api.example.com' },
    DatabaseType: 'SQLLite',
    HasConnectionString: false,
    MaxAllowedFileSizeInKB: 100,
    AuthTokenExpireMinutes: 60,
    EmailConfirmationRedirectUrl: null,
    MailServer: null,
    MailServerPort: null,
    MailFromAddress: null,
    MailUserName: null,
    HasMailPassword: false,
    MailFromDisplayName: null,
  }
}

const shop = app('Shop', 'a1b2-shop', 'Anna@example.com')
const blog = app('Blog', 'c3d4-blog', 'ben@example.com')
const orphan = app('Orphan', 'e5f6-none', null)
const all = [shop, blog, orphan]

function names(applications: AdminApplication[]): string[] {
  return applications.map((application) => application.Name)
}

describe('searchApplications', () => {
  it('keeps every application, in the same order, for an empty search', () => {
    expect(names(searchApplications(all, ''))).toEqual(['Shop', 'Blog', 'Orphan'])
    expect(names(searchApplications(all, '   '))).toEqual(['Shop', 'Blog', 'Orphan'])
  })

  it('finds by name, ignoring case', () => {
    expect(names(searchApplications(all, 'sho'))).toEqual(['Shop'])
  })

  it('finds by token', () => {
    expect(names(searchApplications(all, 'C3D4'))).toEqual(['Blog'])
  })

  it('finds by owner e-mail, ignoring case', () => {
    expect(names(searchApplications(all, 'anna@'))).toEqual(['Shop'])
    expect(names(searchApplications(all, 'example.com'))).toEqual(['Shop', 'Blog'])
  })

  it('ignores spaces around the search', () => {
    expect(names(searchApplications(all, '  blog '))).toEqual(['Blog'])
  })

  it('finds an application without an owner e-mail by its name or token only', () => {
    expect(names(searchApplications(all, 'orphan'))).toEqual(['Orphan'])
    expect(names(searchApplications(all, 'null'))).toEqual([])
  })

  it('returns nothing when no application matches', () => {
    expect(searchApplications(all, 'nothing-like-this')).toEqual([])
  })
})

describe('countText', () => {
  it('says how many there are', () => {
    expect(countText(12, 12)).toBe('12 applications')
    expect(countText(1, 1)).toBe('1 application')
  })

  it('says how many of them a search shows', () => {
    expect(countText(3, 12)).toBe('3 of 12 applications')
    expect(countText(0, 1)).toBe('0 of 1 application')
  })
})

describe('applicationDetails', () => {
  function detail(application: AdminApplication, label: string) {
    return applicationDetails(application).find((row) => row.label === label)
  }

  it('lists the settings in a fixed order', () => {
    expect(applicationDetails(shop).map((row) => row.label)).toEqual([
      'ID',
      'Connection string',
      'Maximum file size',
      'Auth token lifetime',
      'Email confirmation redirect URL',
      'Mail server',
      'Mail server port',
      'Mail sender address',
      'Mail user name',
      'Mail password',
      'Mail sender display name',
    ])
  })

  it('shows numbers with their unit', () => {
    expect(detail(shop, 'ID')).toEqual({ label: 'ID', value: '1' })
    expect(detail(shop, 'Maximum file size')?.value).toBe('100 KB')
    expect(detail(shop, 'Auth token lifetime')?.value).toBe('60 minutes')
  })

  it('says whether a secret is stored, never its value', () => {
    const mysql: AdminApplication = { ...shop, DatabaseType: 'MySQL', HasConnectionString: true, HasMailPassword: true }

    expect(detail(mysql, 'Connection string')).toEqual({ label: 'Connection string', stored: true })
    expect(detail(mysql, 'Mail password')).toEqual({ label: 'Mail password', stored: true })
    expect(detail({ ...mysql, HasConnectionString: false }, 'Connection string')?.stored).toBe(false)
    expect(detail(shop, 'Mail password')?.stored).toBe(false)
  })

  it('says an SQLite application needs no connection string', () => {
    expect(detail(shop, 'Connection string')).toEqual({ label: 'Connection string', value: 'Not needed' })
  })

  it('has null for a setting that is not set, an empty text included', () => {
    expect(detail(shop, 'Mail server')?.value).toBeNull()
    expect(detail(shop, 'Mail server port')?.value).toBeNull()
    expect(detail({ ...shop, MailServer: '  ' }, 'Mail server')?.value).toBeNull()
  })

  it('shows the mail settings that are set', () => {
    const mail: AdminApplication = { ...shop, MailServer: 'smtp.example.com', MailServerPort: 587 }

    expect(detail(mail, 'Mail server')?.value).toBe('smtp.example.com')
    expect(detail(mail, 'Mail server port')?.value).toBe('587')
  })
})
