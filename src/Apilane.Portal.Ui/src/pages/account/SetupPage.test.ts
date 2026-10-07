// @vitest-environment jsdom
import { flushPromises, mount } from '@vue/test-utils'
import type { VueWrapper } from '@vue/test-utils'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import SetupPage from './SetupPage.vue'

const calls = vi.hoisted(() => ({ post: vi.fn(), replace: vi.fn(), setSession: vi.fn(), complete: vi.fn() }))
vi.mock('vue-router', () => ({ useRouter: () => ({ replace: calls.replace }) }))
vi.mock('@/lib/api', async (original) => ({
  ...await original<typeof import('@/lib/api')>(),
  api: { POST: calls.post },
}))
vi.mock('@/lib/session', () => ({ setSession: calls.setSession }))
vi.mock('@/lib/bootstrap', () => ({ completeBootstrap: calls.complete }))

let wrapper: VueWrapper | undefined

beforeEach(() => {
  vi.clearAllMocks()
  calls.replace.mockResolvedValue(undefined)
})

afterEach(() => {
  wrapper?.unmount()
  document.body.innerHTML = ''
})

async function fill(email = 'alex@my-company.test'): Promise<void> {
  wrapper = mount(SetupPage, { attachTo: document.body })
  await wrapper.get('input[name="email"]').setValue(email)
  await wrapper.get('input[name="temporary-password"]').setValue('operator-credential')
  await wrapper.get('input[name="new-password"]').setValue('chosen-password')
  await wrapper.get('input[name="confirm-password"]').setValue('chosen-password')
}

describe('administrator setup', () => {
  it('asks the administrator to choose an email rather than use a configured address', () => {
    wrapper = mount(SetupPage, { attachTo: document.body })

    expect((wrapper.get('input[name="email"]').element as HTMLInputElement).value).toBe('')
    expect(wrapper.text()).toContain('Choose the email address and password for your administrator account.')
    expect(wrapper.text()).toContain('Use the email address you want to sign in with.')
    expect(wrapper.text()).not.toMatch(/startup output contains the administrator email|configured (administrator )?email/i)
  })

  it('keeps the form open after rejected credentials and offers no skip action', async () => {
    calls.post.mockResolvedValue({
      error: { Code: 'UNAUTHORIZED', Message: 'The temporary password is not valid.' },
      response: new Response(null, { status: 401 }),
    })
    await fill()
    await wrapper?.get('form').trigger('submit')
    await flushPromises()

    expect(wrapper?.text()).toContain('The temporary password is not valid.')
    expect(wrapper?.findAll('a')).toHaveLength(0)
    expect(calls.setSession).not.toHaveBeenCalled()
    expect(calls.complete).not.toHaveBeenCalled()
    expect(calls.replace).not.toHaveBeenCalled()
  })

  it('sends the chosen email and continues only after the server accepts the credential and password', async () => {
    const chosenEmail = 'owner@company.test'
    const session = { Email: chosenEmail, IsAdmin: true, InstanceTitle: 'Apilane', Version: '1' }
    let submitted: unknown
    calls.post.mockImplementation(async (_path: string, request: { body: unknown }) => {
      submitted = JSON.parse(JSON.stringify(request.body))
      return { data: session, response: new Response(null, { status: 200 }) }
    })
    await fill(chosenEmail)
    await wrapper?.get('form').trigger('submit')
    await flushPromises()

    expect(calls.post).toHaveBeenCalledWith('/api/v1/bootstrap', expect.anything())
    expect(submitted).toEqual({
      Email: chosenEmail, TemporaryPassword: 'operator-credential',
      Password: 'chosen-password', ConfirmPassword: 'chosen-password',
    })
    expect(calls.setSession).toHaveBeenCalledWith(session)
    expect(calls.complete).toHaveBeenCalledOnce()
    expect(calls.replace).toHaveBeenCalledWith({ name: 'apps' })
    expect((wrapper?.get('input[name="temporary-password"]').element as HTMLInputElement).value).toBe('')
  })
})
