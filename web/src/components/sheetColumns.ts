import type { ScheduleProject, ScheduleTask } from '../api/types'
import { dateTime, durationDays, formatFieldValue, predecessorToken } from '../lib/format'

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
  /** Logical section in the Columns dialog (Identity, Schedule, Work & Cost, ...). */
  group: string
}

/** Built-in columns the sheet can show; users pick a subset (persisted server-side, per project). */
export const BUILTIN_COLUMNS: readonly SheetColumn[] = [
  { key: 'mode', label: '✔', width: 34, group: 'Identity', render: (t) => (t.mode === 'manual' ? '✋' : '') },
  { key: 'row', label: 'ID', width: 44, mono: true, group: 'Identity', render: (t) => String(t.row) },
  { key: 'uid', label: 'UID', width: 52, mono: true, group: 'Identity', render: (t) => String(t.uid) },
  { key: 'wbs', label: 'WBS', width: 64, mono: true, group: 'Identity', render: (t) => t.wbs },
  { key: 'name', label: 'Name', width: 260, group: 'Identity', render: (t) => t.name },
  {
    key: 'duration',
    label: 'Duration',
    width: 80,
    mono: true,
    numeric: true,
    group: 'Schedule',
    render: (t, c) => durationDays(t.durationMinutes, c.minutesPerDay, t.estimated),
  },
  { key: 'start', label: 'Start', width: 130, mono: true, group: 'Schedule', render: (t) => dateTime(t.start) },
  { key: 'finish', label: 'Finish', width: 130, mono: true, group: 'Schedule', render: (t) => dateTime(t.finish) },
  {
    key: 'predecessors',
    label: 'Predecessors',
    width: 110,
    mono: true,
    group: 'Schedule',
    render: (t, c) =>
      t.predecessors
        .map((link) => predecessorToken(c.rowByUid.get(link.predecessorUid) ?? 0, link.type, link.lagKind, link.lagValue, c.minutesPerDay))
        .join(','),
  },
  {
    key: 'totalSlack',
    label: 'Slack',
    width: 70,
    mono: true,
    numeric: true,
    group: 'Schedule',
    render: (t, c) => (t.totalSlackMinutes === null ? '' : durationDays(t.totalSlackMinutes, c.minutesPerDay)),
  },
  { key: 'deadline', label: 'Deadline', width: 110, mono: true, group: 'Schedule', render: (t) => dateTime(t.deadline) },
  {
    key: 'constraint',
    label: 'Constraint',
    width: 130,
    mono: true,
    group: 'Schedule',
    render: (t) => (t.constraint === 'asSoonAsPossible' ? '' : t.constraint),
  },
  {
    key: 'percentComplete',
    label: '%',
    width: 48,
    mono: true,
    numeric: true,
    group: 'Tracking',
    render: (t) => (t.percentComplete > 0 ? `${t.percentComplete}%` : ''),
  },
  {
    key: 'work',
    label: 'Work',
    width: 70,
    mono: true,
    group: 'Work & Cost',
    render: (t) => (t.workMinutes > 0 ? `${Math.round((t.workMinutes / 60) * 10) / 10}h` : ''),
  },
  {
    key: 'cost',
    label: 'Cost',
    width: 80,
    mono: true,
    group: 'Work & Cost',
    render: (t) => (t.cost !== 0 ? String(Math.round(t.cost * 100) / 100) : ''),
  },
  {
    key: 'resourceNames',
    label: 'Resources',
    width: 140,
    group: 'Resources',
    render: (t) => t.assignments.map((a) => a.resource).join(', '),
  },
] as const

export const DEFAULT_COLUMN_KEYS = ['mode', 'row', 'name', 'duration', 'start', 'finish', 'predecessors', 'percentComplete'] as const

/** Built-ins plus one column per custom field, formatted from `task.customValues` by kind. */
export function columnsForProject(project: ScheduleProject): SheetColumn[] {
  const customColumns: SheetColumn[] = project.customFields.map((field) => ({
    key: field.id,
    label: field.alias ?? field.id,
    width: 100,
    group: 'Custom Fields',
    render: (task) => formatFieldValue(field.kind, task.customValues?.[field.id]),
  }))
  return [...BUILTIN_COLUMNS, ...customColumns]
}

export function columnsFor(keys: string[], available: readonly SheetColumn[] = BUILTIN_COLUMNS): SheetColumn[] {
  return keys
    .map((key) => available.find((column) => column.key === key))
    .filter((column): column is SheetColumn => column !== undefined)
}

export function sheetWidth(columns: readonly SheetColumn[]): number {
  return columns.reduce((sum, column) => sum + column.width, 0)
}
