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
