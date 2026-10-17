// @vitest-environment jsdom
import { mount } from '@vue/test-utils'
import type { VueWrapper } from '@vue/test-utils'
import { afterEach, beforeEach, describe, expect, it } from 'vitest'
import { createMemoryHistory, createRouter } from 'vue-router'
import type { Router } from 'vue-router'
import { installDomShims, settle } from '@/test/dom'
import RecordPaging from './RecordPaging.vue'

// The paging bar with a real router: what matters is that a link which reaches the end of the
// records stays the same element, so the keyboard focus stays on it.
let wrapper: VueWrapper | undefined
let router: Router

beforeEach(() => {
  installDomShims()
  router = createRouter({ history: createMemoryHistory(), routes: [{ path: '/records', component: { render: () => null } }] })
})

afterEach(() => {
  wrapper?.unmount()
  wrapper = undefined
})

async function open(address: string): Promise<void> {
  await router.push(address)
  await router.isReady()
  wrapper = mount(RecordPaging, { props: { total: 45, pageSize: 15 }, global: { plugins: [router] }, attachTo: document.body })
  await settle()
}

function link(title: string): HTMLAnchorElement {
  const found = document.body.querySelector<HTMLAnchorElement>(`a[title="${title}"]`)

  if (!found) {
    throw new Error(`Link "${title}" was not found`)
  }

  return found
}

describe('RecordPaging', () => {
  it('keeps the focused Next link in place, disabled, when it reaches the last page', async () => {
    await open('/records?page=2')

    const next = link('Next page')
    next.focus()
    expect(next.getAttribute('aria-disabled')).toBeNull()
    expect(next.getAttribute('href')).toBe('/records?page=3')

    await router.push('/records?page=3')
    await settle()

    expect(link('Next page')).toBe(next)
    expect(document.activeElement).toBe(next)
    expect(next.getAttribute('aria-disabled')).toBe('true')
    expect(next.tabIndex).toBe(-1)
    // Enter on it goes nowhere: it points at the page on screen.
    expect(next.getAttribute('href')).toBe('/records?page=3')
  })

  it('disables First and Previous on page 1 and leaves them pointing at it', async () => {
    await open('/records')

    for (const title of ['First page', 'Previous page']) {
      expect(link(title).getAttribute('aria-disabled')).toBe('true')
      expect(link(title).getAttribute('href')).toBe('/records')
    }

    expect(link('Next page').getAttribute('aria-disabled')).toBeNull()
    expect(link('Last page').getAttribute('href')).toBe('/records?page=3')
  })
})
