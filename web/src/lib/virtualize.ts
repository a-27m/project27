// Fixed-row-height windowing for the task sheet and the Gantt rows.

export interface RowWindow {
  /** First rendered row index (inclusive). */
  first: number
  /** Last rendered row index (exclusive). */
  last: number
  /** Pixel offset of the first rendered row. */
  offsetY: number
  /** Total scrollable height. */
  totalHeight: number
}

export function windowRange(
  scrollTop: number,
  viewportHeight: number,
  rowHeight: number,
  rowCount: number,
  overscan = 5,
): RowWindow {
  const first = Math.max(0, Math.floor(scrollTop / rowHeight) - overscan)
  const visible = Math.ceil(viewportHeight / rowHeight) + 2 * overscan
  const last = Math.min(rowCount, first + visible)
  return {
    first,
    last,
    offsetY: first * rowHeight,
    totalHeight: rowCount * rowHeight,
  }
}
