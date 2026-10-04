import { describe, expect, it } from 'vitest'
import { isAgent, isAgentName } from './agents'

// The same names as tests/Apilane.Portal.Tests/AgentsApiTests.cs.
describe('isAgentName', () => {
  it('accepts lower-case letters, digits and dashes, from 3 to 40 characters', () => {
    expect(isAgentName('a-1')).toBe(true)
    expect(isAgentName('deploy-bot')).toBe(true)
    expect(isAgentName('the-longest-name-an-agent-can-have-40-ch')).toBe(true)
  })

  it('refuses a name that is too short or too long', () => {
    expect(isAgentName('')).toBe(false)
    expect(isAgentName('ab')).toBe(false)
    expect(isAgentName('a-name-that-is-one-character-too-long-41c')).toBe(false)
  })

  it('refuses every other character', () => {
    for (const name of ['Bot', 'my bot', 'my_bot', 'bot.one', 'bot@agent.local', ' bot', 'bot\n']) {
      expect(isAgentName(name), name).toBe(false)
    }
  })
})

describe('isAgent', () => {
  it('goes by the end of the address, whatever the letter case', () => {
    expect(isAgent('deploy-bot@agent.local')).toBe(true)
    expect(isAgent('Deploy-Bot@Agent.LOCAL')).toBe(true)
    expect(isAgent('someone@example.com')).toBe(false)
    expect(isAgent('agent.local@example.com')).toBe(false)
  })
})
