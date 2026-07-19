// Browser-local collapse of summary subtrees. Pure math over the flat schedule task
// list (pre-order + outlineLevel), mirroring outline.ts. Collapse is a view concern:
// it hides a summary's descendants from the sheet/Gantt but never mutates the model.

import type { OutlineTask } from './outline'

/** Uids hidden because they descend from a collapsed summary. */
export function hiddenUids(tasks: readonly OutlineTask[], collapsed: ReadonlySet<number>): Set<number> {
  const hidden = new Set<number>()
  // Same run-skipping shape as pruneNested: once inside a collapsed summary's run,
  // every deeper task is hidden; a shallower/equal level ends the run.
  let hideDeeperThan: number | null = null
  for (const task of tasks) {
    if (hideDeeperThan !== null && task.outlineLevel > hideDeeperThan) {
      hidden.add(task.uid)
      continue
    }
    hideDeeperThan = null
    if (collapsed.has(task.uid)) hideDeeperThan = task.outlineLevel
  }
  return hidden
}

/** The task list with descendants of collapsed summaries removed, order preserved. */
export function visibleTasks<T extends OutlineTask>(tasks: readonly T[], collapsed: ReadonlySet<number>): T[] {
  const hidden = hiddenUids(tasks, collapsed)
  return tasks.filter((task) => !hidden.has(task.uid))
}

/** True iff the task is a summary (the next task in pre-order is one level deeper). */
export function isCollapsible(tasks: readonly OutlineTask[], uid: number): boolean {
  const at = tasks.findIndex((task) => task.uid === uid)
  if (at < 0) return false
  const next = tasks[at + 1]
  return next !== undefined && next.outlineLevel > tasks[at].outlineLevel
}
