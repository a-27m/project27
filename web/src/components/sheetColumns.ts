import type { ScheduleTask } from '../api/types'
import { dateTime, durationDays, predecessorToken } from '../lib/format'

export interface ColumnContext {
  minutesPerDay: number
  rowByUid: ReadonlyMap<number, number>
}

export interface SheetColumn {
  key: string
  label: string
  width: number
  render: (task: ScheduleTask, ctx: ColumnContext) => string
  /** IDs, dates, durations, percentages — mono data cells (IDE-grade density). */
  mono?: boolean
  /** Right-align header + data (the quantities: Dur, %, Slk). */
  numeric?: boolean
}

/** Everything the sheet can show; users pick a subset (persisted per browser). */
export const AVAILABLE_COLUMNS: readonly SheetColumn[] = [
  { key: 'mode', label: '✔', width: 34, render: (t) => (t.mode === 'manual' ? '✋' : '') },
  { key: 'row', label: 'ID', width: 44, mono: true, render: (t) => String(t.row) },
  { key: 'uid', label: 'UID', width: 52, mono: true, render: (t) => String(t.uid) },
  { key: 'wbs', label: 'WBS', width: 64, mono: true, render: (t) => t.wbs },
  { key: 'name', label: 'Name', width: 260, render: (t) => t.name },
  {
    key: 'duration',
    label: 'Duration',
    width: 80,
    mono: true,
    numeric: true,
    render: (t, c) => durationDays(t.durationMinutes, c.minutesPerDay, t.estimated),
  },
  { key: 'start', label: 'Start', width: 130, mono: true, render: (t) => dateTime(t.start) },
  { key: 'finish', label: 'Finish', width: 130, mono: true, render: (t) => dateTime(t.finish) },
  {
    key: 'predecessors',
    label: 'Predecessors',
    width: 110,
    mono: true,
    render: (t, c) =>
      t.predecessors
        .map((link) => predecessorToken(c.rowByUid.get(link.predecessorUid) ?? 0, link.type, link.lagKind, link.lagValue, c.minutesPerDay))
        .join(','),
  },
  {
    key: 'percentComplete',
    label: '%',
    width: 48,
    mono: true,
    numeric: true,
    render: (t) => (t.percentComplete > 0 ? `${t.percentComplete}%` : ''),
  },
  {
    key: 'work',
    label: 'Work',
    width: 70,
    mono: true,
    render: (t) => (t.workMinutes > 0 ? `${Math.round((t.workMinutes / 60) * 10) / 10}h` : ''),
  },
  { key: 'cost', label: 'Cost', width: 80, mono: true, render: (t) => (t.cost !== 0 ? String(Math.round(t.cost * 100) / 100) : '') },
  {
    key: 'totalSlack',
    label: 'Slack',
    width: 70,
    mono: true,
    numeric: true,
    render: (t, c) => (t.totalSlackMinutes === null ? '' : durationDays(t.totalSlackMinutes, c.minutesPerDay)),
  },
  { key: 'resourceNames', label: 'Resources', width: 140, render: (t) => t.assignments.map((a) => a.resource).join(', ') },
  { key: 'deadline', label: 'Deadline', width: 110, mono: true, render: (t) => dateTime(t.deadline) },
  {
    key: 'constraint',
    label: 'Constraint',
    width: 130,
    mono: true,
    render: (t) => (t.constraint === 'asSoonAsPossible' ? '' : t.constraint),
  },
] as const

export const DEFAULT_COLUMN_KEYS = ['mode', 'row', 'name', 'duration', 'start', 'finish', 'predecessors', 'percentComplete'] as const

const STORAGE_KEY = 'p27.columns'

export function loadColumnKeys(): string[] {
  try {
    const raw = localStorage.getItem(STORAGE_KEY)
    if (raw !== null) {
      const parsed: unknown = JSON.parse(raw)
      if (Array.isArray(parsed) && parsed.every((k) => typeof k === 'string') && parsed.length > 0) {
        return parsed.filter((k) => AVAILABLE_COLUMNS.some((c) => c.key === k))
      }
    }
  } catch {
    /* fall through to defaults */
  }
  return [...DEFAULT_COLUMN_KEYS]
}

export function saveColumnKeys(keys: string[]): void {
  localStorage.setItem(STORAGE_KEY, JSON.stringify(keys))
}

export function columnsFor(keys: string[]): SheetColumn[] {
  return keys
    .map((key) => AVAILABLE_COLUMNS.find((column) => column.key === key))
    .filter((column): column is SheetColumn => column !== undefined)
}

export function sheetWidth(columns: readonly SheetColumn[]): number {
  return columns.reduce((sum, column) => sum + column.width, 0)
}
