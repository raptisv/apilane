import { describe, expect, it } from 'vitest'
import {
  endpointAddress,
  endpointParameters,
  highlightMatches,
  renameLosesRules,
  searchEndpoints,
  testParameters,
  testQueryBody,
} from './customEndpoints'

// The same queries as tests/Apilane.Portal.Tests/CustomEndpointsApiTests.cs.
const noParametersQuery = 'select 1'
const oneParameterQuery = 'select * from Orders where ID = {OrderId} and Owner = {Owner}'
const severalParametersQuery =
  'select {Beta}, {Alpha}, {Beta}, {owner}, {With_Underscore}, {Product1} from Items where Owner = {Owner}'

const server = 'https://api.example.com'
const token = 'app-token'

describe('endpointParameters', () => {
  it('finds none in a query without placeholders', () => {
    expect(endpointParameters(noParametersQuery)).toEqual([])
  })

  it('leaves out Owner', () => {
    expect(endpointParameters(oneParameterQuery)).toEqual(['OrderId'])
  })

  it('keeps the first-seen order, lists repeats once, keeps lower-case owner and underscores, skips names with digits', () => {
    expect(endpointParameters(severalParametersQuery)).toEqual(['Beta', 'Alpha', 'owner', 'With_Underscore'])
  })

  it('finds none in an empty or missing query', () => {
    expect(endpointParameters('')).toEqual([])
    expect(endpointParameters(null)).toEqual([])
    expect(endpointParameters(undefined)).toEqual([])
  })

  it('ignores braces without a name and names with spaces', () => {
    expect(endpointParameters('select {}, { A }, {B C}, {_}')).toEqual(['_'])
  })
})

describe('endpointAddress', () => {
  it('gives the plain address and the call address of an endpoint without parameters', () => {
    expect(endpointAddress(server, token, 'Zeta', noParametersQuery)).toEqual({
      Parameters: [],
      Url: `${server}/api/Custom/Zeta`,
      CallUrl: `${server}/api/Custom/Zeta?appToken=${token}`,
    })
  })

  it('adds one placeholder per parameter, after the token in the call address', () => {
    expect(endpointAddress(server, token, 'Alpha', oneParameterQuery)).toEqual({
      Parameters: ['OrderId'],
      Url: `${server}/api/Custom/Alpha?OrderId={OrderId}`,
      CallUrl: `${server}/api/Custom/Alpha?appToken=${token}&OrderId={OrderId}`,
    })
  })

  it('lists several parameters in their order', () => {
    const address = endpointAddress(server, token, 'Multi', severalParametersQuery)

    expect(address.Url).toBe(`${server}/api/Custom/Multi?Beta={Beta}&Alpha={Alpha}&owner={owner}&With_Underscore={With_Underscore}`)
    expect(address.CallUrl).toBe(
      `${server}/api/Custom/Multi?appToken=${token}&Beta={Beta}&Alpha={Alpha}&owner={owner}&With_Underscore={With_Underscore}`,
    )
  })

  it('keeps an appToken parameter of the query after the token, as the Portal does', () => {
    expect(endpointAddress(server, token, 'Tokens', 'select {appToken}')).toEqual({
      Parameters: ['appToken'],
      Url: `${server}/api/Custom/Tokens?appToken={appToken}`,
      CallUrl: `${server}/api/Custom/Tokens?appToken=${token}&appToken={appToken}`,
    })
  })

  it('does not double the slash of a server address that ends in one', () => {
    expect(endpointAddress(`${server}/`, token, 'Zeta', noParametersQuery).Url).toBe(`${server}/api/Custom/Zeta`)
    expect(endpointAddress(`${server}//`, token, 'Zeta', noParametersQuery).CallUrl).toBe(`${server}/api/Custom/Zeta?appToken=${token}`)
  })

  it('trims the name as a save does', () => {
    expect(endpointAddress(server, token, ' Shipments ', 'select * from Shipments where ID = {ShipmentId}\n').Url).toBe(
      `${server}/api/Custom/Shipments?ShipmentId={ShipmentId}`,
    )
  })

  it('checks nothing: an empty name ends in /api/Custom/, an invalid name is used as it is', () => {
    expect(endpointAddress(server, token, '', '')).toEqual({
      Parameters: [],
      Url: `${server}/api/Custom/`,
      CallUrl: `${server}/api/Custom/?appToken=${token}`,
    })
    expect(endpointAddress(server, token, 'alpha 1', 'select {X}').Url).toBe(`${server}/api/Custom/alpha 1?X={X}`)
  })
})

describe('testParameters', () => {
  it('sends every parameter of the query, an empty value for one not typed, and nothing for others', () => {
    expect(testParameters(['A', 'B', 'C'], { A: 5, B: '', Old: 3 })).toEqual({ A: 5, B: '', C: '' })
  })

  it('is empty for a query without parameters', () => {
    expect(testParameters([], { A: 1 })).toEqual({})
  })

  it('passes a long integer on as the text that was typed', () => {
    expect(testParameters(['Id'], { Id: '9007199254740993' })).toEqual({ Id: '9007199254740993' })
  })
})

describe('testQueryBody', () => {
  it('sends the query with a fixed valid name', () => {
    expect(testQueryBody('select 1')).toEqual({ Name: 'test', Query: 'select 1' })
  })
})

const endpoints = [
  { ID: 1, Name: 'Orders', Description: 'All open orders', Query: 'select * from Orders\nwhere Closed = 0' },
  { ID: 2, Name: 'Customers', Description: null, Query: 'SELECT Name FROM Customers' },
  { ID: 3, Name: 'Totals', Query: 'select sum(Total) from Orders (nolock)' },
]

describe('searchEndpoints', () => {
  it('matches nothing for an empty text', () => {
    expect(searchEndpoints(endpoints, '')).toEqual([])
  })

  it('searches the name, the description and the whole query, ignoring letter case', () => {
    expect(searchEndpoints(endpoints, 'customers').map((e) => e.ID)).toEqual([2])
    expect(searchEndpoints(endpoints, 'OPEN').map((e) => e.ID)).toEqual([1])
    expect(searchEndpoints(endpoints, 'from orders').map((e) => e.ID)).toEqual([1, 3])
    expect(searchEndpoints(endpoints, 'closed = 0').map((e) => e.ID)).toEqual([1])
  })

  it('uses the text exactly as typed, spaces and special characters included', () => {
    expect(searchEndpoints(endpoints, 'Orders ').map((e) => e.ID)).toEqual([3])
    expect(searchEndpoints(endpoints, '(nolock)').map((e) => e.ID)).toEqual([3])
    expect(searchEndpoints(endpoints, '*').map((e) => e.ID)).toEqual([1])
    expect(searchEndpoints(endpoints, 'zzz')).toEqual([])
  })
})

describe('highlightMatches', () => {
  it('marks every match, ignoring letter case, and keeps the text around it', () => {
    expect(highlightMatches('select * from Orders; ORDERS', 'orders')).toEqual([
      { text: 'select * from ', match: false },
      { text: 'Orders', match: true },
      { text: '; ', match: false },
      { text: 'ORDERS', match: true },
    ])
  })

  it('treats special characters as text', () => {
    expect(highlightMatches('sum(Total)', '(T')).toEqual([
      { text: 'sum', match: false },
      { text: '(T', match: true },
      { text: 'otal)', match: false },
    ])
  })

  it('gives the whole text unmarked when there is nothing to search', () => {
    expect(highlightMatches('select 1', '')).toEqual([{ text: 'select 1', match: false }])
    expect(highlightMatches('', 'a')).toEqual([])
  })
})

describe('renameLosesRules', () => {
  it('is true for another name', () => {
    expect(renameLosesRules('Orders', 'Sales')).toBe(true)
  })

  it('is false for the same name in another letter case, with spaces around it, or an empty box', () => {
    expect(renameLosesRules('Orders', 'ORDERS')).toBe(false)
    expect(renameLosesRules('Orders', ' Orders ')).toBe(false)
    expect(renameLosesRules('Orders', '  ')).toBe(false)
  })
})
