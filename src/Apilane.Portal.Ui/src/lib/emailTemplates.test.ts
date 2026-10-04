import { describe, expect, it } from 'vitest'
import { templatePlaceholders, templateProblems, visibleTemplates } from './emailTemplates'
import type { EmailTemplate } from './emailTemplates'

function template(ID: number, EventCode: string | null): EmailTemplate {
  return { ID, EventCode, Active: true, Subject: 'Hi', Content: '<p>Hi</p>', Description: 'Email confirmation' }
}

describe('visibleTemplates', () => {
  it('skips a template without an event code and keeps the order', () => {
    const result = visibleTemplates([template(2, 'UserForgotPassword'), template(3, null), template(1, 'UserRegisterConfirmation')])

    expect(result.map((t) => t.ID)).toEqual([2, 1])
  })
})

describe('templatePlaceholders', () => {
  it('lists the user placeholders and the confirmation link for the confirmation e-mail', () => {
    expect(templatePlaceholders('UserRegisterConfirmation').map((p) => p.name)).toEqual([
      '{Users.ID}',
      '{Users.Username}',
      '{Users.Email}',
      '{confirmation_url}',
    ])
  })

  it('lists the reset link for the forgotten password e-mail, with its description', () => {
    const last = templatePlaceholders('UserForgotPassword').at(-1)

    expect(last).toEqual({
      name: '{reset_password_url}',
      description: 'The url that the user has to follow in order to reset the password',
    })
  })

  it('lists nothing for an unknown event, as the classic page', () => {
    expect(templatePlaceholders('Something')).toEqual([])
  })
})

describe('templateProblems', () => {
  it('finds nothing when subject and body are filled in', () => {
    expect(templateProblems({ Subject: 'Welcome', Content: '<p>Hello</p>' })).toEqual({})
  })

  it('marks an empty or blank subject and body as required', () => {
    expect(templateProblems({ Subject: ' ', Content: '' })).toEqual({ Subject: 'Required.', Content: 'Required.' })
  })
})
