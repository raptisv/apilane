import { DOMWrapper, flushPromises } from '@vue/test-utils'
import { nextTick } from 'vue'
import { vi } from 'vitest'

/** jsdom has DOM and focus events, but no layout. Keep these shims local to component tests. */
export function installDomShims(): void {
  vi.stubGlobal('ResizeObserver', class {
    observe(): void {}
    unobserve(): void {}
    disconnect(): void {}
  })
  Object.defineProperty(HTMLElement.prototype, 'scrollIntoView', { configurable: true, value: vi.fn() })
}

export async function settle(): Promise<void> {
  await flushPromises()
  await nextTick()
}

export function body(): DOMWrapper<HTMLElement> {
  return new DOMWrapper(document.body)
}

export function dialog(): DOMWrapper<Element> {
  return new DOMWrapper(body().get('[role="dialog"]').element)
}

export function button(text: string, scope: DOMWrapper<Element> = body()): DOMWrapper<HTMLButtonElement> {
  const match = scope.findAll<HTMLButtonElement>('button').find((candidate) => candidate.text() === text)
  if (!match) {
    throw new Error(`Button "${text}" was not found in ${scope.text()}`)
  }
  return match
}

export function permissionControl(resource: string): DOMWrapper<Element> {
  return new DOMWrapper(body().get(`[role="combobox"][id$="-${resource}"]`).element)
}

export function deletionControl(resource: string): DOMWrapper<Element> {
  return new DOMWrapper(body().get(`[role="checkbox"][id$="-${resource}-delete"]`).element)
}

/** Drive the real Reka select through its keyboard interface, including its teleported menu. */
export async function choosePermission(resource: string, label: string): Promise<void> {
  await permissionControl(resource).trigger('keydown', { key: 'Enter' })
  await settle()
  const option = body().findAll('[role="option"]').find((candidate) => candidate.text() === label)
  if (!option) {
    throw new Error(`Permission "${label}" is not offered for ${resource}`)
  }
  await option.trigger('keydown', { key: 'Enter' })
  await settle()
}
