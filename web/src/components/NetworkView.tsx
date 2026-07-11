import { useMemo } from 'react'
import type { ScheduleTask } from '../api/types'
import { dateOnly, durationDays } from '../lib/format'
import { layoutNetwork } from '../lib/network'

const BOX_WIDTH = 190
const BOX_HEIGHT = 74
const GAP_X = 60
const GAP_Y = 24
const PAD = 24

interface Props {
  tasks: ScheduleTask[]
  minutesPerDay: number
  selectedUid: number | null
  onSelect: (uid: number | null) => void
}

/** Network (PDM) diagram: leaf tasks as boxes in dependency-rank columns. */
export function NetworkView({ tasks, minutesPerDay, selectedUid, onSelect }: Props) {
  const layout = useMemo(() => layoutNetwork(tasks), [tasks])
  const byUid = useMemo(() => new Map(tasks.map((task) => [task.uid, task])), [tasks])
  const positions = useMemo(
    () =>
      new Map(
        layout.nodes.map((node) => [
          node.uid,
          {
            x: PAD + node.rank * (BOX_WIDTH + GAP_X),
            y: PAD + node.lane * (BOX_HEIGHT + GAP_Y),
          },
        ]),
      ),
    [layout],
  )

  if (layout.nodes.length === 0) {
    return <p className="muted pad">No tasks to diagram.</p>
  }

  const width = PAD * 2 + layout.columns * (BOX_WIDTH + GAP_X) - GAP_X
  const height = PAD * 2 + layout.rows * (BOX_HEIGHT + GAP_Y) - GAP_Y

  return (
    <div className="network-scroll">
      <svg width={width} height={height} className="network-svg">
        <defs>
          <marker id="net-arrow" viewBox="0 0 8 8" refX="7" refY="4" markerWidth="7" markerHeight="7" orient="auto">
            <path d="M0 0 L8 4 L0 8 z" fill="var(--link-color)" />
          </marker>
        </defs>
        {layout.edges.map((edge) => {
          const from = positions.get(edge.fromUid)
          const to = positions.get(edge.toUid)
          if (from === undefined || to === undefined) return null
          const x1 = from.x + BOX_WIDTH
          const y1 = from.y + BOX_HEIGHT / 2
          const x2 = to.x
          const y2 = to.y + BOX_HEIGHT / 2
          const bend = x1 + GAP_X / 2
          return (
            <polyline
              key={`${edge.fromUid}-${edge.toUid}`}
              points={`${x1},${y1} ${bend},${y1} ${bend},${y2} ${x2},${y2}`}
              className="gantt-link"
              markerEnd="url(#net-arrow)"
            />
          )
        })}
        {layout.nodes.map((node) => {
          const task = byUid.get(node.uid)
          const position = positions.get(node.uid)
          if (task === undefined || position === undefined) return null
          const selected = task.uid === selectedUid
          return (
            <g
              key={node.uid}
              transform={`translate(${position.x}, ${position.y})`}
              className={
                'network-node' +
                (task.critical ? ' critical' : '') +
                (task.milestone ? ' milestone' : '') +
                (selected ? ' selected' : '')
              }
              onClick={() => onSelect(task.uid === selectedUid ? null : task.uid)}
            >
              <rect width={BOX_WIDTH} height={BOX_HEIGHT} rx={6} />
              <text x={10} y={20} className="network-title">
                {task.name.length > 22 ? task.name.slice(0, 21) + '…' : task.name}
              </text>
              <text x={10} y={40} className="network-meta">
                #{task.row} · {durationDays(task.durationMinutes, minutesPerDay)}
                {task.percentComplete > 0 ? ` · ${task.percentComplete}%` : ''}
              </text>
              <text x={10} y={58} className="network-meta">
                {dateOnly(task.start)} → {dateOnly(task.finish)}
              </text>
            </g>
          )
        })}
      </svg>
    </div>
  )
}
