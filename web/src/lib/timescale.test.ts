import { describe, expect, it } from 'vitest'
import { addDays, dateAt, dayAt, makeScale, monthTicks, startOfDay, ticks, xOf } from './timescale'

describe('timescale', () => {
  const jan5 = new Date(2026, 0, 5, 8, 0) // Mon 2026-01-05 08:00
  const jan16 = new Date(2026, 0, 16, 17, 0)
  const scale = makeScale(jan5, jan16, 24, 3)

  it('pads and sizes the scale in whole days', () => {
    expect(scale.start).toEqual(new Date(2026, 0, 2))
    expect(scale.end).toEqual(new Date(2026, 0, 20))
    expect(scale.width).toBe(18 * 24)
  })

  it('maps dates to x and back', () => {
    expect(xOf(scale, new Date(2026, 0, 2))).toBe(0)
    expect(xOf(scale, new Date(2026, 0, 5))).toBe(72)
    expect(xOf(scale, new Date(2026, 0, 5, 12, 0))).toBe(84) // half a day later
    expect(dateAt(scale, 72)).toEqual(new Date(2026, 0, 5))
  })

  it('snaps to day boundaries', () => {
    expect(dayAt(scale, 71)).toEqual(new Date(2026, 0, 5))
    expect(dayAt(scale, 83)).toEqual(new Date(2026, 0, 5))
    expect(dayAt(scale, 85)).toEqual(new Date(2026, 0, 6))
  })

  it('emits day ticks with week-start majors when wide enough', () => {
    const dayTicks = ticks(scale, 1)
    expect(dayTicks).toHaveLength(18)
    const mondays = dayTicks.filter((tick) => tick.major)
    expect(mondays.map((tick) => tick.label)).toEqual(['5', '12', '19'])
  })

  it('falls back to week ticks when days are narrow', () => {
    const narrow = makeScale(jan5, addDays(jan5, 180), 4, 0)
    const weekTicks = ticks(narrow, 1)
    expect(weekTicks.length).toBeGreaterThan(20)
    expect(weekTicks.every((tick) => tick.label.includes('/'))).toBe(true)
  })

  it('lists months for the header band', () => {
    const months = monthTicks(scale)
    expect(months).toHaveLength(1)
    expect(months[0].x).toBe(0) // clamped to the left edge
  })

  it('startOfDay strips the time', () => {
    expect(startOfDay(jan5)).toEqual(new Date(2026, 0, 5))
  })
})
