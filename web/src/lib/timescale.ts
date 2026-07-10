// Pure date ↔ pixel mapping for the Gantt. Dates are wall-clock (no time zones,
// matching the engine); x = 0 at the scale's start-of-day.

export interface TimeScale {
  /** Midnight of the first visible day. */
  start: Date
  /** Exclusive end (midnight after the last visible day). */
  end: Date
  pxPerDay: number
  width: number
}

const DAY_MS = 86_400_000

export function startOfDay(date: Date): Date {
  return new Date(date.getFullYear(), date.getMonth(), date.getDate())
}

export function addDays(date: Date, days: number): Date {
  const result = new Date(date)
  result.setDate(result.getDate() + days)
  return result
}

/** A scale covering [start … end] padded by `padDays` on both sides. */
export function makeScale(start: Date, end: Date, pxPerDay: number, padDays = 3): TimeScale {
  const from = addDays(startOfDay(start), -padDays)
  const to = addDays(startOfDay(end), padDays + 1)
  const days = Math.max(1, Math.round((to.getTime() - from.getTime()) / DAY_MS))
  return { start: from, end: to, pxPerDay, width: days * pxPerDay }
}

export function xOf(scale: TimeScale, date: Date): number {
  return ((date.getTime() - scale.start.getTime()) / DAY_MS) * scale.pxPerDay
}

export function dateAt(scale: TimeScale, x: number): Date {
  return new Date(scale.start.getTime() + (x / scale.pxPerDay) * DAY_MS)
}

/** Snaps an x offset to the nearest day boundary and returns that day's midnight. */
export function dayAt(scale: TimeScale, x: number): Date {
  const days = Math.round(x / scale.pxPerDay)
  return addDays(scale.start, days)
}

export interface Tick {
  x: number
  label: string
  major: boolean
}

/** Day ticks with week-start majors, or week ticks when days get too dense. */
export function ticks(scale: TimeScale, weekStartsOn = 1): Tick[] {
  const result: Tick[] = []
  const daily = scale.pxPerDay >= 18
  for (let day = new Date(scale.start); day < scale.end; day = addDays(day, 1)) {
    const isWeekStart = day.getDay() === weekStartsOn
    if (daily) {
      result.push({ x: xOf(scale, day), label: String(day.getDate()), major: isWeekStart })
    } else if (isWeekStart) {
      result.push({
        x: xOf(scale, day),
        label: `${day.getMonth() + 1}/${day.getDate()}`,
        major: day.getDate() <= 7,
      })
    }
  }
  return result
}

/** First-of-month labels for the upper header band. */
export function monthTicks(scale: TimeScale): Tick[] {
  const result: Tick[] = []
  const cursor = new Date(scale.start.getFullYear(), scale.start.getMonth(), 1)
  for (let month = cursor; month < scale.end; month = new Date(month.getFullYear(), month.getMonth() + 1, 1)) {
    const x = Math.max(0, xOf(scale, month))
    result.push({
      x,
      label: month.toLocaleDateString(undefined, { month: 'short', year: 'numeric' }),
      major: true,
    })
  }
  return result
}
