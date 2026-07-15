/**
 * Distributes a task's percent-complete across its Gantt segments, weighted by each
 * segment's width (pixels or any consistent unit). Segments fill in order: earlier
 * segments fill fully before a later one gets any fill, so an equal-width 3-segment
 * task at 75% fills the first two fully and the third a quarter.
 */
export function computeSegmentFills(segmentWidths: readonly number[], percentComplete: number): number[] {
  const totalWidth = segmentWidths.reduce((sum, width) => sum + width, 0)
  let remaining = totalWidth * (percentComplete / 100)
  return segmentWidths.map((width) => {
    const fill = Math.max(0, Math.min(width, remaining))
    remaining -= fill
    return fill
  })
}
