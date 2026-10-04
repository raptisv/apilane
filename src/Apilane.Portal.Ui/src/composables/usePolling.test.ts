import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { usePolling } from './usePolling'

// There is no component and no browser here: the two lifecycle hooks are run by hand, and
// `document` is the little of it the composable touches.
const hooks = vi.hoisted(() => ({ mounted: [] as (() => void)[], unmounted: [] as (() => void)[] }))

vi.mock('vue', async (importOriginal) => ({
  ...(await importOriginal<typeof import('vue')>()),
  onMounted: (hook: () => void) => hooks.mounted.push(hook),
  onUnmounted: (hook: () => void) => hooks.unmounted.push(hook),
}))

const tab = {
  hidden: false,
  listeners: [] as (() => void)[],
  addEventListener: (_type: string, listener: () => void) => tab.listeners.push(listener),
  removeEventListener: (_type: string, listener: () => void) => {
    tab.listeners = tab.listeners.filter((other) => other !== listener)
  },
}

function mount(): void {
  hooks.mounted.forEach((hook) => hook())
}

function unmount(): void {
  hooks.unmounted.forEach((hook) => hook())
}

function setHidden(hidden: boolean): void {
  tab.hidden = hidden
  tab.listeners.forEach((listener) => listener())
}

/** Lets the answer of the running attempt arrive, without moving the clock. */
async function settle(): Promise<void> {
  await vi.advanceTimersByTimeAsync(0)
}

interface Answer {
  Status: string
}

const options = { intervalMs: 2000, done: (answer: Answer) => answer.Status === 'Completed' }

beforeEach(() => {
  vi.useFakeTimers()
  vi.stubGlobal('document', tab)
  hooks.mounted = []
  hooks.unmounted = []
  tab.hidden = false
  tab.listeners = []
})

afterEach(() => {
  vi.useRealTimers()
  vi.unstubAllGlobals()
})

describe('usePolling', () => {
  it('asks at once, then again after every interval, until the answer is final', async () => {
    const answers = ['Pending', 'CloningData', 'Completed']
    const load = vi.fn(async () => ({ Status: answers.shift() ?? 'Completed' }))

    const { data, loading } = usePolling(load, options)
    expect(loading.value).toBe(true)

    mount()
    await settle()
    expect(load).toHaveBeenCalledTimes(1)
    expect(loading.value).toBe(false)
    expect(data.value).toEqual({ Status: 'Pending' })

    await vi.advanceTimersByTimeAsync(1999)
    expect(load).toHaveBeenCalledTimes(1)

    await vi.advanceTimersByTimeAsync(1)
    expect(load).toHaveBeenCalledTimes(2)
    expect(data.value).toEqual({ Status: 'CloningData' })

    await vi.advanceTimersByTimeAsync(2000)
    expect(data.value).toEqual({ Status: 'Completed' })

    await vi.advanceTimersByTimeAsync(10000)
    expect(load).toHaveBeenCalledTimes(3)
  })

  it('keeps the last answer when an attempt fails, and tries again', async () => {
    const load = vi
      .fn<() => Promise<Answer>>()
      .mockResolvedValueOnce({ Status: 'Pending' })
      .mockRejectedValueOnce(new Error('The Portal could not be reached.'))
      .mockResolvedValue({ Status: 'CloningData' })

    const { data, error } = usePolling(load, options)
    mount()
    await settle()

    await vi.advanceTimersByTimeAsync(2000)
    expect(error.value?.message).toBe('The Portal could not be reached.')
    expect(data.value).toEqual({ Status: 'Pending' })

    await vi.advanceTimersByTimeAsync(2000)
    expect(error.value).toBeUndefined()
    expect(data.value).toEqual({ Status: 'CloningData' })
  })

  it('stops on a failure it is told to give up on, until restart', async () => {
    const gone = new Error('Not found')
    const load = vi.fn<() => Promise<Answer>>().mockRejectedValueOnce(gone).mockResolvedValue({ Status: 'Pending' })

    const { data, error, loading, restart } = usePolling(load, { ...options, giveUp: (e) => e === gone })
    mount()
    await settle()

    expect(error.value).toBe(gone)
    expect(loading.value).toBe(false)

    await vi.advanceTimersByTimeAsync(10000)
    expect(load).toHaveBeenCalledTimes(1)

    restart()
    expect(loading.value).toBe(true)
    expect(error.value).toBeUndefined()

    await settle()
    expect(load).toHaveBeenCalledTimes(2)
    expect(data.value).toEqual({ Status: 'Pending' })
  })

  it('pauses while the tab is hidden and asks at once when it is shown again', async () => {
    const load = vi.fn(async () => ({ Status: 'Pending' }))

    usePolling(load, options)
    mount()
    await settle()
    expect(load).toHaveBeenCalledTimes(1)

    setHidden(true)
    await vi.advanceTimersByTimeAsync(10000)
    expect(load).toHaveBeenCalledTimes(1)

    setHidden(false)
    await settle()
    expect(load).toHaveBeenCalledTimes(2)

    await vi.advanceTimersByTimeAsync(2000)
    expect(load).toHaveBeenCalledTimes(3)
  })

  it('does not ask again for a final answer when the tab is shown again', async () => {
    const load = vi.fn(async () => ({ Status: 'Completed' }))

    usePolling(load, options)
    mount()
    await settle()

    setHidden(true)
    setHidden(false)
    await vi.advanceTimersByTimeAsync(10000)

    expect(load).toHaveBeenCalledTimes(1)
  })

  it('stops when the component goes away, and drops an answer that arrives afterwards', async () => {
    let answer: (value: Answer) => void = () => {}
    const load = vi.fn(() => new Promise<Answer>((resolve) => (answer = resolve)))

    const { data } = usePolling(load, options)
    mount()
    unmount()

    answer({ Status: 'Pending' })
    await vi.advanceTimersByTimeAsync(10000)

    expect(data.value).toBeUndefined()
    expect(load).toHaveBeenCalledTimes(1)
    expect(tab.listeners).toEqual([])
  })
})
