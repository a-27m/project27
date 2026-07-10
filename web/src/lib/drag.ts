// Pure drag state machines for Gantt interactions. Components feed pointer
// events in; on completion a result describes the command to send.

export interface BarDrag {
  kind: 'bar'
  uid: number
  originX: number
  currentX: number
  barStartX: number
}

export interface LinkDrag {
  kind: 'link'
  fromUid: number
  toUid: number | null
  x: number
  y: number
}

export type DragState = BarDrag | LinkDrag | null

/** Minimum pixel movement before a bar drag counts (avoids click jitter). */
export const DRAG_THRESHOLD = 3

export function beginBarDrag(uid: number, pointerX: number, barStartX: number): BarDrag {
  return { kind: 'bar', uid, originX: pointerX, currentX: pointerX, barStartX }
}

export function moveBarDrag(drag: BarDrag, pointerX: number): BarDrag {
  return { ...drag, currentX: pointerX }
}

export function barDragDelta(drag: BarDrag): number {
  return drag.currentX - drag.originX
}

export interface BarDropResult {
  uid: number
  /** New x of the bar start after the drag. */
  newBarStartX: number
}

/** Null when the movement stayed under the threshold (treat as a click). */
export function endBarDrag(drag: BarDrag): BarDropResult | null {
  const delta = barDragDelta(drag)
  if (Math.abs(delta) < DRAG_THRESHOLD) return null
  return { uid: drag.uid, newBarStartX: drag.barStartX + delta }
}

export function beginLinkDrag(fromUid: number, x: number, y: number): LinkDrag {
  return { kind: 'link', fromUid, toUid: null, x, y }
}

export function moveLinkDrag(drag: LinkDrag, x: number, y: number, overUid: number | null): LinkDrag {
  return { ...drag, x, y, toUid: overUid === drag.fromUid ? null : overUid }
}

export interface LinkDropResult {
  predecessorUid: number
  successorUid: number
}

/** Null when released over nothing or over the origin task. */
export function endLinkDrag(drag: LinkDrag): LinkDropResult | null {
  if (drag.toUid === null) return null
  return { predecessorUid: drag.fromUid, successorUid: drag.toUid }
}
