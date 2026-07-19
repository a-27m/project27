import { describe, expect, it } from 'vitest'
import { dateTime, durationDays, formatFieldValue, formatUnits, fromWireDate, predecessorToken, toWireDate } from './format'

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

  it('renders material assignment units, plain or with a per-time suffix', () => {
    expect(formatUnits(10, null)).toBe('10')
    expect(formatUnits(10, 'day')).toBe('10/d')
    expect(formatUnits(2.5, 'hour')).toBe('2.5/h')
    expect(formatUnits(1, 'week')).toBe('1/w')
    expect(formatUnits(1, 'month')).toBe('1/mo')
    expect(formatUnits(1, 'year')).toBe('1/y')
  })

  it('builds MSP-style predecessor tokens', () => {
    expect(predecessorToken(3, 'finishToStart', 'working', 0, 480)).toBe('3')
    expect(predecessorToken(3, 'finishToStart', 'working', 480, 480)).toBe('3FS+1d')
    expect(predecessorToken(5, 'startToStart', 'percent', -50, 480)).toBe('5SS-50%')
    expect(predecessorToken(2, 'finishToFinish', 'elapsed', 1440, 480)).toBe('2FF+1ed')
  })

  it('formats view-engine field values by kind', () => {
    expect(formatFieldValue('Duration', 960)).toBe('2d')
    expect(formatFieldValue('Work', 120)).toBe('2h')
    expect(formatFieldValue('Percent', 50)).toBe('50%')
    expect(formatFieldValue('Date', '2026-01-05T08:00:00')).toBe('2026-01-05 08:00')
    expect(formatFieldValue('Flag', true)).toBe('yes')
    expect(formatFieldValue('Flag', false)).toBe('no')
    expect(formatFieldValue('Cost', 12.345)).toBe('12.35')
    expect(formatFieldValue('Text', 'hi')).toBe('hi')
    expect(formatFieldValue('Text', null)).toBe('')
  })
})
