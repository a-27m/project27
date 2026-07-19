// Pure pointer-drag state + drop resolution for reordering/reparenting task rows.
// The component feeds pointer coordinates in; dropTarget maps a drop location to the
// moveTask payload (or null for an illegal / no-op drop). Mirrors drag.ts conventions.

import type { OutlineTask } from './outline'

export interface RowDrag {
  kind: 'row'
  uid: number
  originX: number
  originY: number
  x: number
  y: number
}

/** Minimum pointer travel before a press counts as a drag (avoids click jitter). */
export const ROW_DRAG_THRESHOLD = 4

export function beginRowDrag(uid: number, x: number, y: number): RowDrag {
  return { kind: 'row', uid, originX: x, originY: y, x, y }
}

export function moveRowDrag(drag: RowDrag, x: number, y: number): RowDrag {
  return { ...drag, x, y }
}

/** Whether the pointer has moved far enough for this to be a drag, not a click. */
export function dragMoved(drag: RowDrag): boolean {
  return Math.hypot(drag.x - drag.originX, drag.y - drag.originY) >= ROW_DRAG_THRESHOLD
}

export interface DropTarget {
  uid: number
  parentUid?: number
  /** Post-removal index among the target parent's children (Core.MoveTask semantics). */
  at: number
  /** Resolved outline level of the drop — drives the indicator's horizontal inset. */
  level: number
}

/** Index just past a task's subtree (first later task at the same or shallower level). */
function subtreeEnd(tasks: readonly OutlineTask[], start: number): number {
  const level = tasks[start].outlineLevel
  let end = start + 1
  while (end < tasks.length && tasks[end].outlineLevel > level) end++
  return end
}

/** Nearest preceding task shallower than `level`, i.e. the parent at that level. */
function parentAt(tasks: readonly OutlineTask[], before: number, level: number): number | null {
  for (let i = before - 1; i >= 0; i--) {
    if (tasks[i].outlineLevel < level) return tasks[i].uid
  }
  return null
}

/** Number of children of `parentUid` at `level` occurring before index `before`. */
function childCountBefore(
  tasks: readonly OutlineTask[],
  before: number,
  level: number,
  parentUid: number | null,
): number {
  let count = 0
  for (let i = 0; i < before; i++) {
    if (tasks[i].outlineLevel === level && parentAt(tasks, i, level) === parentUid) count++
  }
  return count
}

/**
 * Resolve a drop into a moveTask payload, or null for an illegal / no-op drop.
 *
 * - `aboveUid`: the visible task immediately above the drop line, or null (drop at top).
 * - `indentHint`: desired outline level from the pointer's X (clamped to the legal range
 *   for the gap; this is the reparent affordance).
 * - `collapsed`: collapsed summary uids. A collapsed summary is an opaque block — you may
 *   drop before/after it as a sibling, never into it.
 */
export function dropTarget(
  tasks: readonly OutlineTask[],
  draggedUid: number,
  aboveUid: number | null,
  indentHint: number,
  collapsed: ReadonlySet<number>,
): DropTarget | null {
  const dragAt = tasks.findIndex((t) => t.uid === draggedUid)
  if (dragAt < 0) return null
  const dragEnd = subtreeEnd(tasks, dragAt)
  const inSubtree = (index: number) => index >= dragAt && index < dragEnd

  // A drop into the dragged task's own subtree is illegal (Core rejects it too).
  if (aboveUid !== null) {
    const aIdx = tasks.findIndex((t) => t.uid === aboveUid)
    if (aIdx < 0 || inSubtree(aIdx)) return null
  }

  // Compute on the model with the dragged subtree removed: this yields the post-removal
  // sibling indices that Core.MoveTask expects for `at`.
  const rest = tasks.filter((_, i) => !inSubtree(i))

  let insert: number
  let maxLevel: number
  if (aboveUid === null) {
    insert = 0
    maxLevel = 0
  } else {
    const aRest = rest.findIndex((t) => t.uid === aboveUid)
    const above = rest[aRest]
    const next = rest[aRest + 1]
    const aboveIsSummary = next !== undefined && next.outlineLevel > above.outlineLevel
    const opaque = aboveIsSummary && collapsed.has(aboveUid)
    // Past a collapsed summary we skip its (hidden) subtree; otherwise we land directly
    // under `above` — which, for an expanded summary, is the "first child" position.
    insert = opaque ? subtreeEnd(rest, aRest) : aRest + 1
    maxLevel = opaque ? above.outlineLevel : above.outlineLevel + 1
  }

  const below = rest[insert]
  const minLevel = below !== undefined ? below.outlineLevel : 0
  const lo = Math.min(minLevel, maxLevel)
  const level = Math.max(lo, Math.min(maxLevel, Math.round(indentHint)))

  const parentUid = parentAt(rest, insert, level)
  const at = childCountBefore(rest, insert, level, parentUid)

  // No-op: same parent, same slot, same level as where the task started.
  const dragLevel = tasks[dragAt].outlineLevel
  const origParent = parentAt(tasks, dragAt, dragLevel)
  const origIndex = childCountBefore(tasks, dragAt, dragLevel, origParent)
  if (parentUid === origParent && at === origIndex && level === dragLevel) return null

  return {
    uid: draggedUid,
    ...(parentUid !== null ? { parentUid } : {}),
    at,
    level,
  }
}
