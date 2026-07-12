import { useRef, useState } from 'react'
import type { Command, ScheduleTask } from '../api/types'
import {
  beginBarDrag,
  beginLinkDrag,
  endBarDrag,
  endLinkDrag,
  moveBarDrag,
  moveLinkDrag,
  type DragState,
} from '../lib/drag'
import { fromWireDate, toWireDate } from '../lib/format'
import { dayAt, ticks, xOf, type TimeScale } from '../lib/timescale'
import type { RowWindow } from '../lib/virtualize'

interface Props {
  tasks: ScheduleTask[]
  indexByUid: ReadonlyMap<number, number>
  scale: TimeScale
  rowHeight: number
  window_: RowWindow
  editable: boolean
  selectedUids: ReadonlySet<number>
  onSelect: (uid: number | null) => void
  onCommands: (commands: Command[]) => void
}

const BAR_INSET = 6

/** SVG Gantt body: bars, links, drag-to-reschedule, drag-to-link. */
export function Gantt({
  tasks,
  indexByUid,
  scale,
  rowHeight,
  window_,
  editable,
  selectedUids,
  onSelect,
  onCommands,
}: Props) {
  const [drag, setDrag] = useState<DragState>(null)
  const svgRef = useRef<SVGSVGElement>(null)

  const barHeight = rowHeight - 2 * BAR_INSET
  const barY = (index: number) => index * rowHeight + BAR_INSET
  const barMidY = (index: number) => index * rowHeight + rowHeight / 2

  function localPoint(event: React.PointerEvent): { x: number; y: number } {
    const rect = svgRef.current!.getBoundingClientRect()
    return { x: event.clientX - rect.left, y: event.clientY - rect.top }
  }

  function taskAtY(y: number): ScheduleTask | null {
    const index = Math.floor(y / rowHeight)
    return index >= 0 && index < tasks.length ? tasks[index] : null
  }

  function beginBar(task: ScheduleTask, event: React.PointerEvent) {
    if (!editable || task.summary || task.mode === 'manual' || task.start === null) return
    event.currentTarget.setPointerCapture(event.pointerId)
    const { x } = localPoint(event)
    setDrag(beginBarDrag(task.uid, x, xOf(scale, fromWireDate(task.start))))
  }

  function beginLink(task: ScheduleTask, event: React.PointerEvent) {
    if (!editable) return
    event.stopPropagation()
    event.currentTarget.setPointerCapture(event.pointerId)
    const { x, y } = localPoint(event)
    setDrag(beginLinkDrag(task.uid, x, y))
  }

  function pointerMove(event: React.PointerEvent) {
    if (drag === null) return
    const { x, y } = localPoint(event)
    if (drag.kind === 'bar') setDrag(moveBarDrag(drag, x))
    else setDrag(moveLinkDrag(drag, x, y, taskAtY(y)?.uid ?? null))
  }

  function pointerUp() {
    if (drag === null) return
    setDrag(null)
    if (drag.kind === 'bar') {
      const result = endBarDrag(drag)
      if (result === null) {
        onSelect(selectedUids.has(drag.uid) && selectedUids.size === 1 ? null : drag.uid)
        return
      }
      const day = dayAt(scale, result.newBarStartX)
      onCommands([
        { op: 'setTask', uid: result.uid, constraint: 'startNoEarlierThan', constraintDate: toWireDate(day) },
      ])
    } else {
      const result = endLinkDrag(drag)
      if (result !== null) {
        onCommands([{ op: 'link', predecessorUid: result.predecessorUid, successorUid: result.successorUid }])
      }
    }
  }

  const visible = tasks.slice(window_.first, window_.last)
  const visibleUids = new Set(visible.map((task) => task.uid))
  const today = xOf(scale, new Date())

  return (
    <svg
      ref={svgRef}
      className="gantt-svg"
      width={scale.width}
      height={window_.totalHeight}
      onPointerMove={pointerMove}
      onPointerUp={pointerUp}
      onClick={(event) => {
        if (event.target === svgRef.current) onSelect(null)
      }}
    >
      <defs>
        <marker id="arrow" viewBox="0 0 8 8" refX="7" refY="4" markerWidth="7" markerHeight="7" orient="auto">
          <path d="M0 0 L8 4 L0 8 z" fill="var(--link-color)" />
        </marker>
      </defs>

      {/* weekend + grid shading */}
      {ticks(scale).map((tick) =>
        tick.major ? (
          <line
            key={'g' + tick.x}
            x1={tick.x}
            x2={tick.x}
            y1={0}
            y2={window_.totalHeight}
            className="gantt-grid-major"
          />
        ) : null,
      )}
      {today >= 0 && today <= scale.width && (
        <line x1={today} x2={today} y1={0} y2={window_.totalHeight} className="gantt-today" />
      )}

      {/* dependency lines between visible tasks */}
      {visible.flatMap((task) =>
        task.predecessors
          .filter((link) => visibleUids.has(link.predecessorUid))
          .map((link) => {
            const from = tasks[indexByUid.get(link.predecessorUid)!]
            if (from.finish === null || task.start === null) return null
            const x1 = xOf(scale, fromWireDate(from.finish))
            const y1 = barMidY(indexByUid.get(from.uid)!)
            const x2 = xOf(scale, fromWireDate(task.start))
            const y2 = barMidY(indexByUid.get(task.uid)!)
            const bend = x1 + 8
            return (
              <polyline
                key={`${from.uid}-${task.uid}`}
                points={`${x1},${y1} ${bend},${y1} ${bend},${y2} ${Math.max(bend, x2 - 4)},${y2} ${x2},${y2}`}
                className="gantt-link"
                markerEnd="url(#arrow)"
              />
            )
          }),
      )}

      {/* bars */}
      {visible.map((task) => {
        const index = indexByUid.get(task.uid)!
        if (task.start === null || task.finish === null || task.name === '') return null
        const dragging = drag?.kind === 'bar' && drag.uid === task.uid
        const shift = dragging ? drag.currentX - drag.originX : 0
        const selected = selectedUids.has(task.uid)

        if (task.milestone) {
          const x = xOf(scale, fromWireDate(task.finish)) + shift
          const y = barMidY(index)
          const r = barHeight / 2
          return (
            <path
              key={task.uid}
              d={`M${x} ${y - r} L${x + r} ${y} L${x} ${y + r} L${x - r} ${y} z`}
              className={'gantt-milestone' + (selected ? ' selected' : '')}
              onPointerDown={(event) => beginBar(task, event)}
            >
              <title>{`${task.name} — ${task.finish}`}</title>
            </path>
          )
        }

        const x = tasksBarX(scale, task) + shift
        const width = Math.max(2, barWidth(scale, task))
        if (task.summary) {
          const y = barY(index)
          return (
            <path
              key={task.uid}
              d={summaryPath(x, y, width, barHeight)}
              className={'gantt-summary' + (selected ? ' selected' : '')}
              onClick={() => onSelect(selectedUids.has(task.uid) && selectedUids.size === 1 ? null : task.uid)}
            >
              <title>{task.name}</title>
            </path>
          )
        }

        return (
          <g key={task.uid}>
            {task.segments.map((segment, segmentIndex) => (
              <rect
                key={segmentIndex}
                x={xOf(scale, fromWireDate(segment.start)) + shift}
                y={barY(index)}
                width={Math.max(2, xOf(scale, fromWireDate(segment.finish)) - xOf(scale, fromWireDate(segment.start)))}
                height={barHeight}
                rx={2}
                className={
                  'gantt-bar' +
                  (task.critical ? ' critical' : '') +
                  (selected ? ' selected' : '') +
                  (task.active ? '' : ' inactive') +
                  (task.mode === 'manual' ? ' manual' : '') +
                  (editable && task.mode === 'auto' ? ' draggable' : '')
                }
                onPointerDown={(event) => beginBar(task, event)}
              >
                <title>{`${task.name}\n${segment.start} → ${segment.finish}`}</title>
              </rect>
            ))}
            {editable && (
              <circle
                cx={x + width + 5}
                cy={barMidY(index)}
                r={4}
                className={'gantt-linkhandle' + (drag?.kind === 'link' && drag.fromUid === task.uid ? ' active' : '')}
                onPointerDown={(event) => beginLink(task, event)}
              >
                <title>Drag to a task to link (finish-to-start)</title>
              </circle>
            )}
          </g>
        )
      })}

      {/* link drag rubber band */}
      {drag?.kind === 'link' && (
        <line
          x1={drag.x}
          y1={drag.y}
          x2={drag.x}
          y2={drag.y}
          className="hidden"
        />
      )}
      {drag?.kind === 'link' && (
        <RubberBand drag={drag} tasks={tasks} indexByUid={indexByUid} scale={scale} rowHeight={rowHeight} />
      )}
    </svg>
  )
}

function RubberBand({
  drag,
  tasks,
  indexByUid,
  scale,
  rowHeight,
}: {
  drag: { fromUid: number; toUid: number | null; x: number; y: number }
  tasks: ScheduleTask[]
  indexByUid: ReadonlyMap<number, number>
  scale: TimeScale
  rowHeight: number
}) {
  const fromIndex = indexByUid.get(drag.fromUid)
  if (fromIndex === undefined) return null
  const from = tasks[fromIndex]
  if (from.finish === null) return null
  const x1 = xOf(scale, fromWireDate(from.finish))
  const y1 = fromIndex * rowHeight + rowHeight / 2
  return (
    <g className="gantt-rubber">
      <line x1={x1} y1={y1} x2={drag.x} y2={drag.y} markerEnd="url(#arrow)" />
      {drag.toUid !== null && indexByUid.has(drag.toUid) && (
        <rect
          x={0}
          y={indexByUid.get(drag.toUid)! * rowHeight}
          width={scale.width}
          height={rowHeight}
          className="gantt-droprow"
        />
      )}
    </g>
  )
}

function tasksBarX(scale: TimeScale, task: ScheduleTask): number {
  return xOf(scale, fromWireDate(task.start!))
}

function barWidth(scale: TimeScale, task: ScheduleTask): number {
  return xOf(scale, fromWireDate(task.finish!)) - xOf(scale, fromWireDate(task.start!))
}

function summaryPath(x: number, y: number, width: number, height: number): string {
  const drop = height * 0.8
  return (
    `M${x} ${y} L${x + width} ${y} L${x + width} ${y + drop} L${x + width - 5} ${y + height * 0.45} ` +
    `L${x + 5} ${y + height * 0.45} L${x} ${y + drop} z`
  )
}
