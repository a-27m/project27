import type { CalendarDayCell } from '../api/types'

// Pure logic for the Calendar manager dialog (Task 9): hours-interval parsing/formatting,
// date/range display, and recurrence summaries. Kept separate from Managers.tsx so it's
// unit-testable without rendering the dialog.

const INTERVAL_RE = /^(\d{2}):(\d{2})-(\d{2}):(\d{2})$/
const MONTH_ABBR = ['Jan', 'Feb', 'Mar', 'Apr', 'May', 'Jun', 'Jul', 'Aug', 'Sep', 'Oct', 'Nov', 'Dec']
const MONTH_FULL = [
  'January', 'February', 'March', 'April', 'May', 'June',
  'July', 'August', 'September', 'October', 'November', 'December',
]
const WEEKDAY_KEYS = ['sunday', 'monday', 'tuesday', 'wednesday', 'thursday', 'friday', 'saturday']

export interface ParsedHours {
  ok: boolean
  intervals: string[]
  error: string | null
}

/** Parses `"08:00-12:00,13:00-17:00"` into normalized intervals, or a user-facing error. */
export function parseHours(text: string): ParsedHours {
  const trimmed = text.trim()
  if (trimmed === '') {
    return { ok: false, intervals: [], error: 'Enter at least one interval, e.g. 08:00-12:00.' }
  }
  const intervals: string[] = []
  for (const part of trimmed.split(',')) {
    const piece = part.trim()
    const match = INTERVAL_RE.exec(piece)
    if (match === null) {
      return { ok: false, intervals: [], error: `"${piece}" isn't an interval. Use HH:MM-HH:MM, comma-separated.` }
    }
    const [, sh, sm, eh, em] = match
    if (`${eh}:${em}` <= `${sh}:${sm}`) {
      return { ok: false, intervals: [], error: `"${piece}" ends before it starts.` }
    }
    intervals.push(`${sh}:${sm}-${eh}:${em}`)
  }
  return { ok: true, intervals, error: null }
}

/** Total working hours across intervals, rounded to one decimal place. */
export function hoursTotal(intervals: readonly string[]): number {
  let minutes = 0
  for (const interval of intervals) {
    const [start, end] = interval.split('-')
    const [sh, sm] = start.split(':').map(Number)
    const [eh, em] = end.split(':').map(Number)
    minutes += eh * 60 + em - (sh * 60 + sm)
  }
  return Math.round((minutes / 60) * 10) / 10
}

function parseIsoDate(iso: string): Date {
  const [y, m, d] = iso.split('-').map(Number)
  return new Date(y, m - 1, d)
}

export function formatDate(iso: string): string {
  const date = parseIsoDate(iso)
  return `${date.getDate()} ${MONTH_ABBR[date.getMonth()]} ${date.getFullYear()}`
}

/** `null`/equal end = single day; same month = "24-31 Dec 2026"; else full range. */
export function formatDateRange(start: string, end: string | null): string {
  if (end === null || end === start) return formatDate(start)
  const a = parseIsoDate(start)
  const b = parseIsoDate(end)
  if (a.getFullYear() === b.getFullYear() && a.getMonth() === b.getMonth()) {
    return `${a.getDate()}–${b.getDate()} ${MONTH_ABBR[a.getMonth()]} ${a.getFullYear()}`
  }
  return `${formatDate(start)} – ${formatDate(end)}`
}

/** Weekday key (`"monday"`..) of an ISO date, for the implied day of a weekly recurrence. */
export function weekdayKeyOf(iso: string): string {
  return WEEKDAY_KEYS[parseIsoDate(iso).getDay()]
}

/** Day-of-month of an ISO date, for the implied day of a monthly recurrence. */
export function dayOfMonthOf(iso: string): number {
  return parseIsoDate(iso).getDate()
}

export function monthLabel(year: number, month: number): string {
  return `${MONTH_FULL[month - 1]} ${year}`
}

export type RecurrenceKind = 'daily' | 'weekly' | 'monthly'
export type RecurrenceEndMode = 'never' | 'count' | 'date'

export interface RecurrenceDetail {
  /** Weekly only: weekday keys (`"monday"`..) the exception recurs on. */
  days?: readonly string[]
  /** Monthly only: day of month (1-31) the exception recurs on. */
  day?: number
}

/** e.g. "every 2 weeks on Mon, Wed" / "every month on day 15, 12 times" / "every day, until 2026-12-31". */
export function recurrenceSummary(
  kind: RecurrenceKind,
  every: number,
  endMode: RecurrenceEndMode,
  endValue: string,
  detail?: RecurrenceDetail,
): string {
  const unit = kind === 'daily' ? 'day' : kind === 'weekly' ? 'week' : 'month'
  let everyPart = every === 1 ? `every ${unit}` : `every ${every} ${unit}s`
  if (kind === 'weekly' && detail?.days !== undefined && detail.days.length > 0) {
    everyPart += ` on ${detail.days.map((day) => day.slice(0, 3).replace(/^./, (c) => c.toUpperCase())).join(', ')}`
  } else if (kind === 'monthly' && detail?.day !== undefined) {
    everyPart += ` on day ${detail.day}`
  }
  const value = endValue.trim()
  if (endMode === 'count' && value !== '') return `${everyPart}, ${value} times`
  if (endMode === 'date' && value !== '') return `${everyPart}, until ${value}`
  return everyPart
}

export type WeeklyDayTag = 'inherited' | 'overridesBase' | 'sameAsBase' | 'default' | 'setHere' | 'ownHours'

/**
 * Which tag/emphasis a weekly-pattern row gets (design §4): based calendars compare
 * against the base's resolved value; standalone calendars just flag "some days set".
 */
export function weeklyDayTag(hasBase: boolean, dayValue: string, baseValue: string | undefined, allDaysSet: boolean): WeeklyDayTag {
  if (hasBase) {
    if (dayValue === 'inherit') return 'inherited'
    return dayValue === baseValue ? 'sameAsBase' : 'overridesBase'
  }
  if (dayValue === 'inherit') return 'default'
  return allDaysSet ? 'ownHours' : 'setHere'
}

/** Whether a weekly-pattern row should be highlighted as a genuine override (design §4). */
export function weeklyDayIsHighlighted(tag: WeeklyDayTag): boolean {
  return tag === 'overridesBase' || tag === 'setHere'
}

export interface MonthCellStyle {
  fill: string
  textVar: string
  hasMarker: boolean
}

/** Fill/text/marker for a month-preview day cell, by precedence: exception > work week > weekly (design §7). */
export function monthCellStyle(source: CalendarDayCell['source'], working: boolean): MonthCellStyle {
  if (source === 'exception') {
    return working
      ? { fill: 'rgba(66,190,101,0.18)', textVar: '--support-success', hasMarker: true }
      : { fill: 'rgba(250,77,86,0.18)', textVar: '--support-error', hasMarker: true }
  }
  if (source === 'workWeek') {
    return { fill: 'rgba(255,131,43,0.16)', textVar: '--support-caution-major', hasMarker: true }
  }
  return working
    ? { fill: 'var(--layer-02)', textVar: '--text-primary', hasMarker: false }
    : { fill: 'transparent', textVar: '--text-helper', hasMarker: false }
}

/** Number of blank leading cells so a Mon-first month grid aligns under its weekday header. */
export function leadingBlanksForMonth(year: number, month: number): number {
  const firstDayOfWeek = new Date(year, month - 1, 1).getDay() // 0=Sun..6=Sat
  return (firstDayOfWeek + 6) % 7
}

/** Resolved weekly-pattern value for a day: its own setting, else the base's, else "off" (no base). */
export function resolvedWeeklyValue(dayValue: string, baseValue: string | undefined, hasBase: boolean): string {
  if (dayValue !== 'inherit') return dayValue
  if (hasBase && baseValue !== undefined && baseValue !== 'inherit') return baseValue
  return 'off'
}

export function isWorkingValue(value: string): boolean {
  return value !== 'off'
}
