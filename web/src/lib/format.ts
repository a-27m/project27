// Rendering helpers mirroring the CLI's conventions (invariant, days in "0.##d").

export function durationDays(minutes: number, minutesPerDay: number, estimated = false): string {
  const days = minutes / minutesPerDay
  const rounded = Math.round(days * 100) / 100
  return `${trimNumber(rounded)}d${estimated ? '?' : ''}`
}

export function trimNumber(value: number): string {
  return Number.isInteger(value) ? String(value) : String(value)
}

const RATE_UNIT_SUFFIX: Record<string, string> = {
  hour: '/h',
  day: '/d',
  week: '/w',
  month: '/mo',
  year: '/y',
}

/** Material assignment units, e.g. "10" (fixed) or "10/d" (variable consumption); mirrors the CLI's Render.PerSuffix. */
export function formatUnits(units: number, unitsPer: string | null): string {
  return trimNumber(units) + (unitsPer === null ? '' : (RATE_UNIT_SUFFIX[unitsPer] ?? ''))
}

/** "2026-01-05 08:00" from an ISO wall-clock string; empty for null. */
export function dateTime(iso: string | null): string {
  if (iso === null) return ''
  return iso.slice(0, 10) + ' ' + iso.slice(11, 16)
}

export function dateOnly(iso: string | null): string {
  return iso === null ? '' : iso.slice(0, 10)
}

/** Wall-clock ISO string (no zone suffix) the server expects. */
export function toWireDate(date: Date): string {
  const pad = (n: number) => String(n).padStart(2, '0')
  return (
    `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}` +
    `T${pad(date.getHours())}:${pad(date.getMinutes())}:00`
  )
}

/** Parses the engine's wall-clock ISO into a local Date (no zone shifts). */
export function fromWireDate(iso: string): Date {
  return new Date(
    Number(iso.slice(0, 4)),
    Number(iso.slice(5, 7)) - 1,
    Number(iso.slice(8, 10)),
    Number(iso.slice(11, 13) || '0'),
    Number(iso.slice(14, 16) || '0'),
  )
}

const PREDECESSOR_ABBREVIATION: Record<string, string> = {
  finishToStart: 'FS',
  startToStart: 'SS',
  finishToFinish: 'FF',
  startToFinish: 'SF',
}

export function predecessorToken(
  row: number,
  type: string,
  lagKind: string,
  lagValue: number,
  minutesPerDay: number,
): string {
  const zeroLag = lagValue === 0
  if (type === 'finishToStart' && zeroLag) return String(row)
  let lag = ''
  if (!zeroLag) {
    const value =
      lagKind === 'percent'
        ? `${trimNumber(lagValue)}%`
        : lagKind === 'elapsed'
          ? `${trimNumber(Math.round((lagValue / 1440) * 100) / 100)}ed`
          : `${trimNumber(Math.round((lagValue / minutesPerDay) * 100) / 100)}d`
    lag = (lagValue < 0 ? '' : '+') + value
  }
  return `${row}${PREDECESSOR_ABBREVIATION[type] ?? '?'}${lag}`
}

/** Base duration units accepted in lag text input, mirroring the CLI's Duration unit aliases. */
const BASE_LAG_UNITS: Record<string, 'm' | 'h' | 'd' | 'w' | 'mo'> = {
  m: 'm',
  min: 'm',
  mins: 'm',
  minute: 'm',
  minutes: 'm',
  h: 'h',
  hr: 'h',
  hrs: 'h',
  hour: 'h',
  hours: 'h',
  d: 'd',
  dy: 'd',
  dys: 'd',
  day: 'd',
  days: 'd',
  w: 'w',
  wk: 'w',
  wks: 'w',
  week: 'w',
  weeks: 'w',
  mo: 'mo',
  mon: 'mo',
  mons: 'mo',
  month: 'mo',
  months: 'mo',
}

/** Renders a lag as editable text: "3d", "50%", "2ed", "-1h"; zero lag is blank. Mirrors predecessorToken's units. */
export function formatLagInput(kind: string, value: number, minutesPerDay: number): string {
  if (value === 0) return ''
  if (kind === 'percent') return `${trimNumber(value)}%`
  if (kind === 'elapsed') return `${trimNumber(Math.round((value / 1440) * 100) / 100)}ed`
  return `${trimNumber(Math.round((value / minutesPerDay) * 100) / 100)}d`
}

/**
 * Parses lag text ("3d", "4eh", "50%", leading "-" for lead) into wire minutes/percent.
 * Working weeks/months aren't convertible client-side (minutesPerWeek/daysPerMonth aren't
 * exposed to the web app), so those unit tokens are rejected; elapsed weeks/months use fixed
 * calendar-independent constants and are supported.
 */
export function parseLagInput(text: string, minutesPerDay: number): { kind: 'working' | 'elapsed' | 'percent'; value: number } | null {
  const trimmed = text.trim()
  if (trimmed === '' || trimmed === '0') return { kind: 'working', value: 0 }

  const lead = trimmed.startsWith('-')
  const body = lead ? trimmed.slice(1) : trimmed

  if (body.endsWith('%')) {
    const percent = Number(body.slice(0, -1))
    if (body.length < 2 || !Number.isFinite(percent)) return null
    return { kind: 'percent', value: lead ? -percent : percent }
  }

  const match = /^(\d+(?:\.\d+)?)\s*([a-zA-Z]+)$/.exec(body)
  if (match === null) return null
  const value = Number(match[1])
  const unitToken = match[2].toLowerCase()
  const elapsed = unitToken.length > 1 && unitToken.startsWith('e') && unitToken.slice(1) in BASE_LAG_UNITS
  const base = BASE_LAG_UNITS[elapsed ? unitToken.slice(1) : unitToken]
  if (base === undefined) return null

  let minutes: number
  if (base === 'm') minutes = value
  else if (base === 'h') minutes = value * 60
  else if (base === 'd') minutes = elapsed ? value * 1440 : value * minutesPerDay
  else if (base === 'w') {
    if (!elapsed) return null
    minutes = value * 10080
  } else {
    if (!elapsed) return null
    minutes = value * 43200
  }

  return { kind: elapsed ? 'elapsed' : 'working', value: lead ? -minutes : minutes }
}

/** Field kinds rendered right-aligned in grids (server FieldKind names, e.g. from /fields, /view). */
export const NUMERIC_FIELD_KINDS = new Set(['Number', 'Cost', 'Work', 'Duration', 'Percent', 'WholeNumber'])

/** Formats a raw view-engine value (built-in or custom field) by its FieldKind. */
export function formatFieldValue(kind: string, raw: unknown): string {
  if (raw === null || raw === undefined) return ''
  switch (kind) {
    case 'Duration':
      return `${Math.round((Number(raw) / 480) * 100) / 100}d`
    case 'Work':
      return `${Math.round((Number(raw) / 60) * 100) / 100}h`
    case 'Percent':
      return `${String(raw)}%`
    case 'Date':
      return String(raw).slice(0, 16).replace('T', ' ')
    case 'Flag':
      return raw === true ? 'yes' : 'no'
    case 'Cost':
    case 'Number':
      return String(Math.round(Number(raw) * 100) / 100)
    default:
      return String(raw)
  }
}
