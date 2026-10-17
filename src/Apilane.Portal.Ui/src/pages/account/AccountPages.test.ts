// @vitest-environment jsdom
import { mount } from '@vue/test-utils'
import type { VueWrapper } from '@vue/test-utils'
import type { Component } from 'vue'
import { ref, shallowRef } from 'vue'
import { createMemoryHistory, createRouter } from 'vue-router'
import type { Router } from 'vue-router'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import type { Schemas } from '@/lib/api'
import { body, settle } from '@/test/dom'
import ForgotPasswordPage from './ForgotPasswordPage.vue'
import LoginPage from './LoginPage.vue'
import RegisterPage from './RegisterPage.vue'

const calls = vi.hoisted(() => ({ post: vi.fn() }))
vi.mock('@/lib/api', async (original) => ({
  ...await original<typeof import('@/lib/api')>(),
  api: { POST: calls.post },
}))

// What GET /instance answered. useInstance shares one load for the whole app, so the tests set it here.
const instance = shallowRef<Schemas['InstanceResponse']>()
vi.mock('@/composables/useInstance', () => ({
  useInstance: () => ({ data: instance, error: shallowRef(), loading: ref(false), reload: vi.fn() }),
}))

const deepLink = '/apps/shared-app/security?item=Entity-Orders'
let wrapper: VueWrapper | undefined
let router: Router

async function mountPage(component: Component, path: string): Promise<void> {
  const empty = { render: () => null }
  router = createRouter({
    history: createMemoryHistory(),
    routes: [
      { path: '/account/login', name: 'login', component: empty },
      { path: '/account/register', name: 'register', component: empty },
      { path: '/account/forgot-password', name: 'forgot-password', component: empty },
      { path: '/apps', name: 'apps', component: empty },
      { path: '/:pathMatch(.*)*', component: empty },
    ],
  })
  await router.push(path)
  await router.isReady()
  wrapper = mount(component, { attachTo: document.body, global: { plugins: [router] } })
  await settle()
}

function link(text: string): string | undefined {
  return body().findAll('a').find((candidate) => candidate.text() === text)?.attributes('href')
}

/** The screen an address opens and the returnUrl it carries, as the router reads them. */
function target(href: string | undefined): { name: unknown; returnUrl: unknown } {
  const resolved = router.resolve(href ?? '/')

  return { name: resolved.name, returnUrl: resolved.query.returnUrl }
}

beforeEach(() => {
  vi.resetAllMocks()
  instance.value = { InstanceTitle: 'Portal', AllowRegister: true, IsMailSetup: true }
})

afterEach(() => {
  wrapper?.unmount()
  wrapper = undefined
  document.body.innerHTML = ''
})

describe('sign in', () => {
  it('carries the address the visitor was sent to on to Sign up', async () => {
    await mountPage(LoginPage, `/account/login?returnUrl=${encodeURIComponent(deepLink)}`)

    expect(target(link('Sign up'))).toEqual({ name: 'register', returnUrl: deepLink })
  })

  it('carries no address that is not a path on this site', async () => {
    await mountPage(LoginPage, `/account/login?returnUrl=${encodeURIComponent('https://elsewhere.test/')}`)

    expect(link('Sign up')).toBe('/account/register')
  })

  it('offers a password reset only when the instance can send mail', async () => {
    await mountPage(LoginPage, '/account/login')
    expect(link('Forgot password?')).toBe('/account/forgot-password')

    instance.value = { InstanceTitle: 'Portal', AllowRegister: true, IsMailSetup: false }
    await settle()
    expect(link('Forgot password?')).toBeUndefined()
    expect(body().text()).toContain('This instance cannot send email: ask an administrator of this instance.')
  })
})

describe('sign up', () => {
  it('opens the address the new user was sent to, and carries it on to Sign in', async () => {
    const session: Schemas['SessionResponse'] = { Email: 'bob@example.test', IsAdmin: false, InstanceTitle: 'Portal', Version: '1' }
    calls.post.mockResolvedValue({ data: session, response: new Response(null, { status: 200 }) })
    await mountPage(RegisterPage, `/account/register?returnUrl=${encodeURIComponent(deepLink)}`)
    expect(target(link('Sign in'))).toEqual({ name: 'login', returnUrl: deepLink })

    await body().get('input[name="email"]').setValue('bob@example.test')
    await body().get('input[name="new-password"]').setValue('a-new-password')
    await body().get('input[name="confirm-password"]').setValue('a-new-password')
    await body().get('form').trigger('submit')
    await settle()

    expect(calls.post).toHaveBeenCalledOnce()
    expect(router.currentRoute.value.fullPath).toBe(deepLink)
  })

  it('opens the applications without an address to return to', async () => {
    const session: Schemas['SessionResponse'] = { Email: 'bob@example.test', IsAdmin: false, InstanceTitle: 'Portal', Version: '1' }
    calls.post.mockResolvedValue({ data: session, response: new Response(null, { status: 200 }) })
    await mountPage(RegisterPage, '/account/register')

    await body().get('input[name="email"]').setValue('bob@example.test')
    await body().get('input[name="new-password"]').setValue('a-new-password')
    await body().get('input[name="confirm-password"]').setValue('a-new-password')
    await body().get('form').trigger('submit')
    await settle()

    expect(router.currentRoute.value.name).toBe('apps')
  })
})

describe('forgot password', () => {
  it('says that no reset link can be sent instead of offering the form when mail is not set up', async () => {
    instance.value = { InstanceTitle: 'Portal', AllowRegister: true, IsMailSetup: false }
    await mountPage(ForgotPasswordPage, '/account/forgot-password')

    expect(body().text()).toContain('Password reset is not available')
    expect(body().find('form').exists()).toBe(false)
    expect(calls.post).not.toHaveBeenCalled()
  })

  it('offers the form when mail is set up', async () => {
    await mountPage(ForgotPasswordPage, '/account/forgot-password')

    expect(body().find('form').exists()).toBe(true)
  })
})
