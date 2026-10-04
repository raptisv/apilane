// The e-mail templates of an application. They live on its API server, not in the Portal: the
// e-mail screen reads them with GET {ServerUrl}/api/Email/GetEmails and saves one with
// PUT {ServerUrl}/api/Email/Update (through lib/apiServer.ts), as the classic page does.

/** A template as GET /api/Email/GetEmails answers it. */
export interface EmailTemplate {
  ID: number
  /** 'UserRegisterConfirmation' or 'UserForgotPassword'. */
  EventCode: string | null
  Active: boolean
  Subject: string | null
  Content: string | null
  /** What the e-mail is for, in words: 'Email confirmation'. */
  Description: string
}

/** The body of PUT /api/Email/Update. */
export interface EmailTemplateUpdate {
  ID: number
  EventCode: string
  Active: boolean
  Subject: string
  Content: string
}

/** A text the API server replaces in the subject and the body when it sends the e-mail. */
export interface Placeholder {
  name: string
  description: string
}

/** The templates the screen lists: the classic page skips one without an event code. */
export function visibleTemplates(templates: readonly EmailTemplate[]): EmailTemplate[] {
  return templates.filter((template) => template.EventCode !== null)
}

const userPlaceholders: Placeholder[] = [
  { name: '{Users.ID}', description: 'The user id' },
  { name: '{Users.Username}', description: 'The username' },
  { name: '{Users.Email}', description: 'The user email' },
]

// A copy of EmailEvent.EmailEvents (src/Apilane.Common/Helpers/EmailEvent.cs), which the API server
// fills the placeholders from. Both events are sent to the user who triggered them, so neither
// has the {Users.From.*} placeholders. Add an event here when one is added there.
const eventPlaceholders: Record<string, Placeholder[]> = {
  UserRegisterConfirmation: [
    { name: '{confirmation_url}', description: 'The url that the user has to follow in order to confirm the email address' },
  ],
  UserForgotPassword: [
    { name: '{reset_password_url}', description: 'The url that the user has to follow in order to reset the password' },
  ],
}

/** The placeholders a template can use, as the classic page lists them. None for an unknown event. */
export function templatePlaceholders(eventCode: string): Placeholder[] {
  const own = eventPlaceholders[eventCode]

  return own ? [...userPlaceholders, ...own] : []
}

/**
 * The fields of a template the API server would reject as empty, checked before sending. It names
 * the body wrongly when it rejects an empty one, so the form could not mark that field otherwise.
 */
export function templateProblems(form: { Subject: string; Content: string }): Record<string, string> {
  const problems: Record<string, string> = {}

  if (form.Subject.trim() === '') {
    problems.Subject = 'Required.'
  }

  if (form.Content.trim() === '') {
    problems.Content = 'Required.'
  }

  return problems
}
