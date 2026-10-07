// @vitest-environment jsdom
import { mount } from '@vue/test-utils'
import type { VueWrapper } from '@vue/test-utils'
import { defineComponent, h, ref } from 'vue'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import type { AgentPermissionGrant } from '@/lib/agentPermissions'
import { permissionResources, readOnlyPolicy } from '@/test/agentPermissions'
import { body, choosePermission, deletionControl, installDomShims, permissionControl, settle } from '@/test/dom'
import AgentPermissionsEditor from './AgentPermissionsEditor.vue'

let wrapper: VueWrapper | undefined
const grants = ref<AgentPermissionGrant[]>([])

beforeEach(() => {
  installDomShims()
  grants.value = readOnlyPolicy()
  wrapper = mount(defineComponent({
    setup: () => () => h(AgentPermissionsEditor, {
      resources: permissionResources,
      modelValue: grants.value,
      'onUpdate:modelValue': (value: AgentPermissionGrant[]) => { grants.value = value },
    }),
  }), { attachTo: document.body })
})

afterEach(() => {
  wrapper?.unmount()
  document.body.innerHTML = ''
  vi.unstubAllGlobals()
})

function grant(resource: string): AgentPermissionGrant | undefined {
  return grants.value.find((item) => item.Resource === resource)
}

describe('AgentPermissionsEditor controls', () => {
  it('keeps deletion independent of write and clears it when read is removed', async () => {
    await settle()
    await deletionControl('entities').trigger('click')
    expect(grant('entities')).toEqual({ Resource: 'entities', Read: true, Write: false, Delete: true })
    expect(body().text()).toContain('Deletion cannot be undone.')

    await choosePermission('entities', 'Read and write')
    expect(grant('entities')).toEqual({ Resource: 'entities', Read: true, Write: true, Delete: true })
    await choosePermission('entities', 'Read')
    expect(grant('entities')).toEqual({ Resource: 'entities', Read: true, Write: false, Delete: true })
    await choosePermission('entities', 'None')
    expect(grant('entities')).toEqual({ Resource: 'entities', Read: false, Write: false, Delete: false })
    expect(deletionControl('entities').attributes('disabled')).toBeDefined()
    expect(body().text()).not.toContain('Deletion cannot be undone.')

    await deletionControl('entities').trigger('click')
    expect(grant('entities')?.Delete).toBe(false)
    await choosePermission('entities', 'Read')
    expect(deletionControl('entities').attributes('disabled')).toBeUndefined()
    expect(grant('entities')?.Delete).toBe(false)
  })

  it.each(['audit-log', 'email-settings'])('offers only read access for %s and supported deletion controls', async (resource) => {
    await settle()
    expect(body().findAll('[role="checkbox"]')).toHaveLength(3)
    expect(body().find('[role="checkbox"][id$="-security-delete"]').exists()).toBe(false)
    await permissionControl(resource).trigger('keydown', { key: 'Enter' })
    await settle()
    expect(body().findAll('[role="option"]').map((option) => option.text())).toEqual(['None', 'Read'])
  })

  it('grants rebuilding separately without inventing read or delete rights and displays its warning', async () => {
    await settle()
    expect(permissionControl('rebuild').text()).toBe('None')
    await choosePermission('rebuild', 'Allow')
    expect(grant('rebuild')).toEqual({ Resource: 'rebuild', Read: false, Write: true, Delete: false })
    expect(body().text()).toContain('permanently removing all its data.')
    expect(grant('application')).toEqual({ Resource: 'application', Read: false, Write: false, Delete: false })
    await choosePermission('rebuild', 'None')
    expect(grant('rebuild')?.Write).toBe(false)
    expect(body().text()).not.toContain('permanently removing all its data.')
  })

  it('changes only the chosen area and grants read when enabling write', async () => {
    await settle()
    await choosePermission('security', 'None')
    await choosePermission('security', 'Read and write')
    expect(grant('security')).toEqual({ Resource: 'security', Read: true, Write: true, Delete: false })
    expect(grants.value.filter((item) => item.Resource !== 'security'))
      .toEqual(readOnlyPolicy().filter((item) => item.Resource !== 'security'))
  })
})
