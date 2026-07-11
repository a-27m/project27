// Pure layout for the network (PDM) diagram: leaf tasks in columns by dependency
// rank (longest predecessor chain), ordered within a column by row number.

export interface NetworkInput {
  uid: number
  row: number
  summary: boolean
  predecessors: { predecessorUid: number }[]
}

export interface NetworkNode {
  uid: number
  /** Column index: longest path length over included predecessors. */
  rank: number
  /** Position within the column, 0-based. */
  lane: number
}

export interface NetworkEdge {
  fromUid: number
  toUid: number
}

export interface NetworkLayout {
  nodes: NetworkNode[]
  edges: NetworkEdge[]
  columns: number
  /** Height of the tallest column, in nodes. */
  rows: number
}

/** Summaries are excluded (their links are inherited by leaves in the engine). */
export function layoutNetwork(tasks: NetworkInput[]): NetworkLayout {
  const leaves = tasks.filter((task) => !task.summary)
  const byUid = new Map(leaves.map((task) => [task.uid, task]))
  const ranks = new Map<number, number>()

  const rankOf = (uid: number, guard: number): number => {
    const known = ranks.get(uid)
    if (known !== undefined) return known
    if (guard > leaves.length) return 0 // cycle guard; the engine prevents cycles anyway
    const task = byUid.get(uid)
    if (task === undefined) return 0
    let rank = 0
    for (const link of task.predecessors) {
      if (byUid.has(link.predecessorUid)) {
        rank = Math.max(rank, rankOf(link.predecessorUid, guard + 1) + 1)
      }
    }
    ranks.set(uid, rank)
    return rank
  }

  const nodes: NetworkNode[] = []
  const laneCounters = new Map<number, number>()
  for (const task of [...leaves].sort((a, b) => a.row - b.row)) {
    const rank = rankOf(task.uid, 0)
    const lane = laneCounters.get(rank) ?? 0
    laneCounters.set(rank, lane + 1)
    nodes.push({ uid: task.uid, rank, lane })
  }

  const edges: NetworkEdge[] = leaves.flatMap((task) =>
    task.predecessors
      .filter((link) => byUid.has(link.predecessorUid))
      .map((link) => ({ fromUid: link.predecessorUid, toUid: task.uid })),
  )

  return {
    nodes,
    edges,
    columns: nodes.length === 0 ? 0 : Math.max(...nodes.map((n) => n.rank)) + 1,
    rows: laneCounters.size === 0 ? 0 : Math.max(...laneCounters.values()),
  }
}
