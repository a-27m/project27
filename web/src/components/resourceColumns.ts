import type { ColumnOption } from './ColumnsDialog'

/** Every column the Resources grid can show; users pick a subset (persisted server-side, per project). */
export const RESOURCE_COLUMNS: readonly ColumnOption[] = [
  { key: 'name', label: 'Name' },
  { key: 'type', label: 'Type' },
  { key: 'maxUnits', label: 'Max units' },
  { key: 'rate', label: 'Rate' },
  { key: 'initials', label: 'Initials' },
  { key: 'group', label: 'Group' },
  { key: 'calendar', label: 'Calendar' },
  { key: 'materialLabel', label: 'Material label' },
  { key: 'accrual', label: 'Accrual' },
]

export const DEFAULT_RESOURCE_COLUMN_KEYS = RESOURCE_COLUMNS.map((c) => c.key)
