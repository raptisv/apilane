import { describe, expect, it } from 'vitest'
import { toFormData } from './formData'

describe('toFormData', () => {
  it('sends text and numbers as text', () => {
    const form = toFormData({ ServerID: 3, DatabaseType: 'MySQL' })

    expect(form.get('ServerID')).toBe('3')
    expect(form.get('DatabaseType')).toBe('MySQL')
  })

  it('leaves out null and undefined', () => {
    const form = toFormData({ ConnectionString: null, Other: undefined, Empty: '' })

    expect(form.has('ConnectionString')).toBe(false)
    expect(form.has('Other')).toBe(false)
    expect(form.get('Empty')).toBe('')
  })

  it('keeps a file as a file, with its name', () => {
    const file = new File(['{}'], 'application.json', { type: 'application/json' })
    const sent = toFormData({ File: file }).get('File')

    expect(sent).toBeInstanceOf(File)
    expect((sent as File).name).toBe('application.json')
  })
})
