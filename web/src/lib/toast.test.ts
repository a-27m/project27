import { describe, expect, it } from 'vitest'
import { errorMessage, initialToastState, MAX_TOASTS, toastReducer } from './toast'

describe('toastReducer', () => {
  it('appends a toast with an incrementing id', () => {
    const first = toastReducer(initialToastState, { type: 'add', message: 'one', variant: 'info' })
    expect(first.toasts).toEqual([{ id: 1, message: 'one', variant: 'info' }])
    const second = toastReducer(first, { type: 'add', message: 'two', variant: 'error' })
    expect(second.toasts).toEqual([
      { id: 1, message: 'one', variant: 'info' },
      { id: 2, message: 'two', variant: 'error' },
    ])
    expect(second.nextId).toBe(3)
  })

  it('dismisses a toast by id', () => {
    const added = toastReducer(initialToastState, { type: 'add', message: 'one', variant: 'info' })
    const dismissed = toastReducer(added, { type: 'dismiss', id: 1 })
    expect(dismissed.toasts).toEqual([])
  })

  it('is a no-op when dismissing an id that is not present', () => {
    const added = toastReducer(initialToastState, { type: 'add', message: 'one', variant: 'info' })
    const dismissed = toastReducer(added, { type: 'dismiss', id: 999 })
    expect(dismissed.toasts).toEqual(added.toasts)
  })

  it('caps the stack at MAX_TOASTS, dropping the oldest', () => {
    let state = initialToastState
    for (let i = 0; i < MAX_TOASTS + 2; i++) {
      state = toastReducer(state, { type: 'add', message: `msg${i}`, variant: 'info' })
    }
    expect(state.toasts).toHaveLength(MAX_TOASTS)
    expect(state.toasts[0].message).toBe('msg2')
    expect(state.toasts.at(-1)?.message).toBe(`msg${MAX_TOASTS + 1}`)
  })
})

describe('errorMessage', () => {
  it('uses the message of an Error', () => {
    expect(errorMessage(new Error('boom'))).toBe('boom')
  })

  it('stringifies a non-Error cause', () => {
    expect(errorMessage('plain string')).toBe('plain string')
    expect(errorMessage(404)).toBe('404')
  })
})
