import { useMemo } from 'react'
import type { ScheduleTask } from '../api/types'
import { fromWireDate } from '../lib/format'
import { assignLanes } from '../lib/lanes'
import { makeScale, monthTicks, ticks, xOf } from '../lib/timescale'

const LANE_HEIGHT = 34
const HEADER = 44
const PAD_BOTTOM = 16

interface Props {
  tasks: ScheduleTask[]
  projectStart: string
  projectFinish: string | null
  selectedUid: number | null
  onSelect: (uid: number | null) => void
}

/** Timeline band: top-level tasks lane-packed over a month scale; milestones as diamonds. */
export function TimelineView({ tasks, projectStart, projectFinish, selectedUid, onSelect }: Props) {
  const topLevel = useMemo(
    () => tasks.filter((task) => task.outlineLevel === 0 && task.start !== null && task.finish !== null),
    [tasks],
  )

  const scale = useMemo(() => {
    const start = fromWireDate(projectStart)
    const finish = projectFinish !== null ? fromWireDate(projectFinish) : start
    const days = Math.max(1, (finish.getTime() - start.getTime()) / 86_400_000)
    const pxPerDay = Math.min(48, Math.max(6, 1100 / days))
    return makeScale(start, finish, pxPerDay)
  }, [projectStart, projectFinish])

  const { lanes, laneCount } = useMemo(
    () =>
      assignLanes(
        topLevel.map((task) => ({
          uid: task.uid,
          start: fromWireDate(task.start!).getTime(),
          end: fromWireDate(task.finish!).getTime(),
        })),
      ),
    [topLevel],
  )
  const laneOf = useMemo(() => new Map(lanes.map((l) => [l.uid, l.lane])), [lanes])

  if (topLevel.length === 0) {
    return <p className="muted pad">No scheduled top-level tasks.</p>
  }

  const height = HEADER + Math.max(1, laneCount) * LANE_HEIGHT + PAD_BOTTOM

  return (
    <div className="network-scroll">
      <svg width={scale.width} height={height} className="timeline-svg">
        {monthTicks(scale).map((tick) => (
          <text key={'m' + tick.x} x={tick.x + 4} y={16} className="gantt-month">
            {tick.label}
          </text>
        ))}
        {ticks(scale)
          .filter((tick) => tick.major)
          .map((tick) => (
            <line key={tick.x} x1={tick.x} x2={tick.x} y1={HEADER - 18} y2={height} className="gantt-grid" />
          ))}
        <line x1={0} x2={scale.width} y1={HEADER - 2} y2={HEADER - 2} className="gantt-grid-major" />

        {topLevel.map((task) => {
          const lane = laneOf.get(task.uid) ?? 0
          const y = HEADER + lane * LANE_HEIGHT + 4
          const selected = task.uid === selectedUid
          if (task.milestone) {
            const x = xOf(scale, fromWireDate(task.finish!))
            const cy = y + (LANE_HEIGHT - 8) / 2
            const r = 9
            return (
              <g key={task.uid} onClick={() => onSelect(task.uid === selectedUid ? null : task.uid)}>
                <path
                  d={`M${x} ${cy - r} L${x + r} ${cy} L${x} ${cy + r} L${x - r} ${cy} z`}
                  className={'gantt-milestone' + (selected ? ' selected' : '')}
                />
                <text x={x + 12} y={cy + 4} className="timeline-label">
                  {task.name}
                </text>
              </g>
            )
          }

          const x1 = xOf(scale, fromWireDate(task.start!))
          const x2 = xOf(scale, fromWireDate(task.finish!))
          const width = Math.max(4, x2 - x1)
          return (
            <g key={task.uid} onClick={() => onSelect(task.uid === selectedUid ? null : task.uid)}>
              <rect
                x={x1}
                y={y}
                width={width}
                height={LANE_HEIGHT - 10}
                rx={5}
                className={
                  'timeline-bar' + (task.summary ? ' summary' : '') + (task.critical ? ' critical' : '') + (selected ? ' selected' : '')
                }
              />
              <text x={x1 + 6} y={y + (LANE_HEIGHT - 10) / 2 + 4} className="timeline-bartext" clipPath={undefined}>
                {width > task.name.length * 7 ? task.name : ''}
              </text>
              {width <= task.name.length * 7 && (
                <text x={x2 + 6} y={y + (LANE_HEIGHT - 10) / 2 + 4} className="timeline-label">
                  {task.name}
                </text>
              )}
            </g>
          )
        })}
      </svg>
    </div>
  )
}
