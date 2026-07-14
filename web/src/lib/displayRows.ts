// Expands the flat task list into display rows for the fixed-row-height virtualizer:
// each task row, followed by its cosmetic spaceAfter as inert gap rows. Gaps carry no
// uid — they are never scheduled, linked, selected, or navigated to; see the task's
// `formatting.spaceAfter` in Project27.Core (never read by CPM, Network, or WBS).

export interface SpacedTask {
  uid: number
  spaceAfter: number
}

export interface TaskRow<T> {
  kind: 'task'
  task: T
}

export interface GapRow {
  kind: 'gap'
  /** Uid of the task this gap trails, for a stable React key. */
  afterUid: number
  /** 0-based position within that task's run of gap rows. */
  index: number
}

export type DisplayRow<T> = TaskRow<T> | GapRow

export function buildDisplayRows<T extends SpacedTask>(tasks: readonly T[]): DisplayRow<T>[] {
  const rows: DisplayRow<T>[] = []
  for (const task of tasks) {
    rows.push({ kind: 'task', task })
    for (let index = 0; index < task.spaceAfter; index++) {
      rows.push({ kind: 'gap', afterUid: task.uid, index })
    }
  }
  return rows
}

/** Uid -> position in the display-row list, for windowing and Gantt row alignment. */
export function displayIndexByUid<T extends SpacedTask>(rows: readonly DisplayRow<T>[]): Map<number, number> {
  const byUid = new Map<number, number>()
  rows.forEach((row, index) => {
    if (row.kind === 'task') byUid.set(row.task.uid, index)
  })
  return byUid
}
