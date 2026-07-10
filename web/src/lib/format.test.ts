import { describe, expect, it } from 'vitest'
import { dateTime, durationDays, fromWireDate, predecessorToken, toWireDate } from './format'

describe('format', () => {
  it('renders durations in days from minutes', () => {
    expect(durationDays(960, 480)).toBe('2d')
    expect(durationDays(1200, 480)).toBe('2.5d')
    expect(durationDays(480, 480, true)).toBe('1d?')
  })

  it('renders wall-clock date-times', () => {
    expect(dateTime('2026-01-05T08:00:00')).toBe('2026-01-05 08:00')
    expect(dateTime(null)).toBe('')
  })

  it('round-trips wire dates without zone shifts', () => {
    const date = fromWireDate('2026-01-05T08:00:00')
    expect(date).toEqual(new Date(2026, 0, 5, 8, 0))
    expect(toWireDate(date)).toBe('2026-01-05T08:00:00')
  })

  it('builds MSP-style predecessor tokens', () => {
    expect(predecessorToken(3, 'finishToStart', 'working', 0, 480)).toBe('3')
    expect(predecessorToken(3, 'finishToStart', 'working', 480, 480)).toBe('3FS+1d')
    expect(predecessorToken(5, 'startToStart', 'percent', -50, 480)).toBe('5SS-50%')
    expect(predecessorToken(2, 'finishToFinish', 'elapsed', 1440, 480)).toBe('2FF+1ed')
  })
})
