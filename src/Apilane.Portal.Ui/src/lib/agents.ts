/**
 * Agents: portal users for scripts and AI agents, which call the API with a key. Copies of the
 * Portal's own rules (PortalAgent and CreateAgentRequest); the API is what enforces them.
 */

/** The rule of an agent's name, as the form shows it. */
export const agentNameRule = '3 to 40 characters: lower-case letters, digits and dashes.'

/** Whether the API accepts this name for a new agent. */
export function isAgentName(name: string): boolean {
  return /^[a-z0-9-]{3,40}$/.test(name)
}

/** Whether a user is an agent: its address ends with @agent.local, whatever the letter case. */
export function isAgent(email: string): boolean {
  return email.toLowerCase().endsWith('@agent.local')
}

/** Agent names for display; keep the original address for API calls and identity comparisons. */
export function accountDisplayName(email: string): string {
  return isAgent(email) ? email.slice(0, -'@agent.local'.length) : email
}
