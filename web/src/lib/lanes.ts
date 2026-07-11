// Greedy interval lane packing for the timeline view: overlapping bars go to
// different lanes; each bar takes the first lane that is free at its start.

export interface LaneInput {
  uid: number
  /** Milliseconds (or any comparable number). */
  start: number
  end: number
}

export interface LaneAssignment {
  uid: number
  lane: number
}

export function assignLanes(bars: LaneInput[]): { lanes: LaneAssignment[]; laneCount: number } {
  const sorted = [...bars].sort((a, b) => a.start - b.start || a.end - b.end)
  const laneEnds: number[] = []
  const lanes: LaneAssignment[] = []
  for (const bar of sorted) {
    let lane = laneEnds.findIndex((end) => end <= bar.start)
    if (lane < 0) {
      lane = laneEnds.length
      laneEnds.push(0)
    }
    laneEnds[lane] = bar.end
    lanes.push({ uid: bar.uid, lane })
  }
  return { lanes, laneCount: laneEnds.length }
}
