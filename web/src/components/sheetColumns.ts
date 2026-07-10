export const SHEET_COLUMNS = [
  { key: 'row', label: 'ID', width: 44 },
  { key: 'name', label: 'Name', width: 260 },
  { key: 'duration', label: 'Duration', width: 80 },
  { key: 'start', label: 'Start', width: 130 },
  { key: 'finish', label: 'Finish', width: 130 },
  { key: 'predecessors', label: 'Predecessors', width: 110 },
] as const

export const SHEET_WIDTH = SHEET_COLUMNS.reduce((sum, column) => sum + column.width, 0)
