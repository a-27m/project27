import { describe, expect, it } from 'vitest'
import {
  dayOfMonthOf,
  formatDate,
  formatDateRange,
  hoursTotal,
  isWorkingValue,
  leadingBlanksForMonth,
  monthCellStyle,
  monthLabel,
  parseHours,
  recurrenceSummary,
  resolvedWeeklyValue,
  weekdayKeyOf,
  weeklyDayIsHighlighted,
  weeklyDayTag,
} from './calendarManager'

describe('parseHours', () => {
  it('parses a single interval', () => {
    expect(parseHours('08:00-12:00')).toEqual({ ok: true, intervals: ['08:00-12:00'], error: null })
  })

  it('parses comma-separated intervals and trims whitespace', () => {
    expect(parseHours('08:00-12:00, 13:00-17:00')).toEqual({
      ok: true,
      intervals: ['08:00-12:00', '13:00-17:00'],
      error: null,
    })
  })

  it('rejects blank input', () => {
    expect(parseHours('  ').ok).toBe(false)
    expect(parseHours('').error).toMatch(/at least one interval/)
  })

  it('rejects a malformed interval', () => {
    const result = parseHours('8-12')
    expect(result.ok).toBe(false)
    expect(result.error).toMatch(/isn't an interval/)
  })

  it('rejects an interval that ends before it starts', () => {
    const result = parseHours('17:00-09:00')
    expect(result.ok).toBe(false)
    expect(result.error).toMatch(/ends before it starts/)
  })
})

describe('hoursTotal', () => {
  it('sums minutes across intervals into hours', () => {
    expect(hoursTotal(['08:00-12:00', '13:00-17:00'])).toBe(8)
  })

  it('rounds to one decimal', () => {
    expect(hoursTotal(['08:00-08:50'])).toBe(0.8)
  })

  it('is zero for no intervals', () => {
    expect(hoursTotal([])).toBe(0)
  })
})

describe('formatDate / formatDateRange', () => {
  it('formats a single date', () => {
    expect(formatDate('2026-07-03')).toBe('3 Jul 2026')
  })

  it('collapses a same-day range to a single date', () => {
    expect(formatDateRange('2026-07-03', '2026-07-03')).toBe('3 Jul 2026')
    expect(formatDateRange('2026-07-03', null)).toBe('3 Jul 2026')
  })

  it('formats a same-month range compactly', () => {
    expect(formatDateRange('2026-12-24', '2026-12-31')).toBe('24–31 Dec 2026')
  })

  it('formats a cross-month range with both full dates', () => {
    expect(formatDateRange('2026-06-29', '2026-07-02')).toBe('29 Jun 2026 – 2 Jul 2026')
  })
})

describe('weekdayKeyOf / dayOfMonthOf', () => {
  it('derives the weekday key from an ISO date', () => {
    // 2026-07-03 is a Friday.
    expect(weekdayKeyOf('2026-07-03')).toBe('friday')
  })

  it('derives the day-of-month from an ISO date', () => {
    expect(dayOfMonthOf('2026-07-03')).toBe(3)
  })
})

describe('monthLabel', () => {
  it('formats month and year', () => {
    expect(monthLabel(2026, 7)).toBe('July 2026')
  })
})

describe('recurrenceSummary', () => {
  it('pluralizes the interval unit', () => {
    expect(recurrenceSummary('weekly', 1, 'never', '')).toBe('every week')
    expect(recurrenceSummary('weekly', 2, 'never', '')).toBe('every 2 weeks')
  })

  it('appends a count end condition', () => {
    expect(recurrenceSummary('monthly', 1, 'count', '12')).toBe('every month, 12 times')
  })

  it('appends a date end condition', () => {
    expect(recurrenceSummary('daily', 1, 'date', '2026-12-31')).toBe('every day, until 2026-12-31')
  })

  it('ignores a blank end value', () => {
    expect(recurrenceSummary('daily', 3, 'count', '  ')).toBe('every 3 days')
  })

  it('lists weekdays for a weekly recurrence', () => {
    expect(recurrenceSummary('weekly', 1, 'never', '', { days: ['monday', 'wednesday'] })).toBe('every week on Mon, Wed')
  })

  it('states the day of month for a monthly recurrence', () => {
    expect(recurrenceSummary('monthly', 2, 'count', '6', { day: 15 })).toBe('every 2 months on day 15, 6 times')
  })

  it('omits the weekday clause when no days are given', () => {
    expect(recurrenceSummary('weekly', 1, 'never', '', { days: [] })).toBe('every week')
  })
})

describe('weeklyDayTag', () => {
  it('tags an inherited day when based', () => {
    expect(weeklyDayTag(true, 'inherit', 'off', false)).toBe('inherited')
  })

  it('tags a day matching the base as same-as-base', () => {
    expect(weeklyDayTag(true, 'off', 'off', false)).toBe('sameAsBase')
  })

  it('tags a day differing from the base as an override', () => {
    expect(weeklyDayTag(true, '08:00-12:00', 'off', false)).toBe('overridesBase')
  })

  it('tags an inherited day as default when standalone', () => {
    expect(weeklyDayTag(false, 'inherit', undefined, false)).toBe('default')
  })

  it('tags a partially-set standalone calendar as set-here', () => {
    expect(weeklyDayTag(false, 'off', undefined, false)).toBe('setHere')
  })

  it('tags a fully-set standalone calendar as own-hours (no signal)', () => {
    expect(weeklyDayTag(false, 'off', undefined, true)).toBe('ownHours')
  })
})

describe('weeklyDayIsHighlighted', () => {
  it('highlights genuine overrides only', () => {
    expect(weeklyDayIsHighlighted('overridesBase')).toBe(true)
    expect(weeklyDayIsHighlighted('setHere')).toBe(true)
    expect(weeklyDayIsHighlighted('inherited')).toBe(false)
    expect(weeklyDayIsHighlighted('sameAsBase')).toBe(false)
    expect(weeklyDayIsHighlighted('default')).toBe(false)
    expect(weeklyDayIsHighlighted('ownHours')).toBe(false)
  })
})

describe('monthCellStyle', () => {
  it('marks a working exception in success colors', () => {
    expect(monthCellStyle('exception', true)).toEqual({ fill: 'rgba(66,190,101,0.18)', textVar: '--support-success', hasMarker: true })
  })

  it('marks an off exception in error colors', () => {
    expect(monthCellStyle('exception', false)).toEqual({ fill: 'rgba(250,77,86,0.18)', textVar: '--support-error', hasMarker: true })
  })

  it('marks a work-week override in caution colors regardless of working', () => {
    expect(monthCellStyle('workWeek', true).textVar).toBe('--support-caution-major')
    expect(monthCellStyle('workWeek', false).textVar).toBe('--support-caution-major')
  })

  it('has no marker for plain weekly-pattern days', () => {
    expect(monthCellStyle('weeklyPattern', true).hasMarker).toBe(false)
    expect(monthCellStyle('none', false).hasMarker).toBe(false)
  })
})

describe('leadingBlanksForMonth', () => {
  it('is zero when the month starts on a Monday', () => {
    // 2026-06-01 is a Monday.
    expect(leadingBlanksForMonth(2026, 6)).toBe(0)
  })

  it('is six when the month starts on a Sunday', () => {
    // 2026-11-01 is a Sunday.
    expect(leadingBlanksForMonth(2026, 11)).toBe(6)
  })

  it('counts back from mid-week starts', () => {
    // 2026-07-01 is a Wednesday.
    expect(leadingBlanksForMonth(2026, 7)).toBe(2)
  })
})

describe('resolvedWeeklyValue / isWorkingValue', () => {
  it('uses the day value when explicitly set', () => {
    expect(resolvedWeeklyValue('off', '08:00-12:00', true)).toBe('off')
  })

  it('falls through to the base when inherited and based', () => {
    expect(resolvedWeeklyValue('inherit', '08:00-12:00', true)).toBe('08:00-12:00')
  })

  it('resolves to off when inherited and standalone', () => {
    expect(resolvedWeeklyValue('inherit', undefined, false)).toBe('off')
  })

  it('treats off as non-working and anything else as working', () => {
    expect(isWorkingValue('off')).toBe(false)
    expect(isWorkingValue('08:00-12:00')).toBe(true)
  })
})
