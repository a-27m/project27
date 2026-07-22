import { describe, expect, it } from 'vitest'
import {
  dateTime,
  durationDays,
  formatFieldValue,
  formatLagInput,
  formatUnits,
  fromWireDate,
  parseLagInput,
  predecessorToken,
  toWireDate,
} from './format'

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

  it('formats lag values as editable text', () => {
    expect(formatLagInput('working', 0, 480)).toBe('')
    expect(formatLagInput('working', 960, 480)).toBe('2d')
    expect(formatLagInput('working', -480, 480)).toBe('-1d')
    expect(formatLagInput('elapsed', 2880, 480)).toBe('2ed')
    expect(formatLagInput('percent', -50, 480)).toBe('-50%')
  })

  it('parses lag text into wire lag values', () => {
    expect(parseLagInput('', 480)).toEqual({ kind: 'working', value: 0 })
    expect(parseLagInput('0', 480)).toEqual({ kind: 'working', value: 0 })
    expect(parseLagInput('2d', 480)).toEqual({ kind: 'working', value: 960 })
    expect(parseLagInput('-2d', 480)).toEqual({ kind: 'working', value: -960 })
    expect(parseLagInput('3h', 480)).toEqual({ kind: 'working', value: 180 })
    expect(parseLagInput('2eh', 480)).toEqual({ kind: 'elapsed', value: 120 })
    expect(parseLagInput('2ed', 480)).toEqual({ kind: 'elapsed', value: 2880 })
    expect(parseLagInput('50%', 480)).toEqual({ kind: 'percent', value: 50 })
    expect(parseLagInput('-50%', 480)).toEqual({ kind: 'percent', value: -50 })
    expect(parseLagInput('2w', 480)).toBeNull()
    expect(parseLagInput('not-a-lag', 480)).toBeNull()
  })

  it('round-trips lag text through format and parse', () => {
    for (const [kind, value] of [
      ['working', 960],
      ['working', -960],
      ['elapsed', 2880],
      ['percent', 50],
      ['percent', -25],
    ] as const) {
      const text = formatLagInput(kind, value, 480)
      expect(parseLagInput(text, 480)).toEqual({ kind, value })
    }
  })
})
