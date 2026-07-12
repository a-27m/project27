// Pure outline math over the flat schedule task list (row order + outlineLevel).

export interface OutlineTask {
  uid: number
  outlineLevel: number
}

export interface OutlinePosition {
  /** Uid of the parent, or null at top level. */
  parentUid: number | null
  /** 0-based index among the siblings. */
  index: number
  /** Sibling uids in order (the task itself included). */
  siblings: number[]
}

/** Parent and sibling position of a task, derived from pre-order + levels. */
export function positionOf(tasks: readonly OutlineTask[], uid: number): OutlinePosition | null {
  const at = tasks.findIndex((task) => task.uid === uid)
  if (at < 0) return null
  const level = tasks[at].outlineLevel

  let parentUid: number | null = null
  for (let i = at - 1; i >= 0; i--) {
    if (tasks[i].outlineLevel < level) {
      parentUid = tasks[i].uid
      break
    }
  }

  const siblings: number[] = []
  for (let i = 0; i < tasks.length; i++) {
    if (tasks[i].outlineLevel === level && parentOf(tasks, i) === parentUid) {
      siblings.push(tasks[i].uid)
    }
  }

  return { parentUid, index: siblings.indexOf(uid), siblings }
}

function parentOf(tasks: readonly OutlineTask[], at: number): number | null {
  const level = tasks[at].outlineLevel
  for (let i = at - 1; i >= 0; i--) {
    if (tasks[i].outlineLevel < level) return tasks[i].uid
  }
  return null
}

/** The moveTask payload to swap a task with its previous/next sibling, or null at the edge. */
export function siblingMove(
  tasks: readonly OutlineTask[],
  uid: number,
  direction: 'up' | 'down',
): { uid: number; parentUid?: number; at: number } | null {
  const position = positionOf(tasks, uid)
  if (position === null) return null
  const target = direction === 'up' ? position.index - 1 : position.index + 1
  if (target < 0 || target >= position.siblings.length) return null
  return {
    uid,
    ...(position.parentUid !== null ? { parentUid: position.parentUid } : {}),
    at: target,
  }
}

/** Expands a shift-click range between two uids over the visible order. */
export function rangeBetween(tasks: readonly OutlineTask[], fromUid: number, toUid: number): number[] {
  const a = tasks.findIndex((task) => task.uid === fromUid)
  const b = tasks.findIndex((task) => task.uid === toUid)
  if (a < 0 || b < 0) return []
  const [start, end] = a <= b ? [a, b] : [b, a]
  return tasks.slice(start, end + 1).map((task) => task.uid)
}

/** Drops uids whose ancestor is also in the set (bulk ops on subtrees hit the root only). */
export function pruneNested(tasks: readonly OutlineTask[], uids: ReadonlySet<number>): number[] {
  const result: number[] = []
  let skipDeeperThan: number | null = null
  for (const task of tasks) {
    if (skipDeeperThan !== null && task.outlineLevel > skipDeeperThan) continue
    skipDeeperThan = null
    if (uids.has(task.uid)) {
      result.push(task.uid)
      skipDeeperThan = task.outlineLevel
    }
  }
  return result
}
