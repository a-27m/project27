import { useCallback, useEffect, useMemo, useRef, useState } from 'react'
import type { ApiClient } from '../api/client'
import type { Command, ProjectInfo, Schedule } from '../api/types'
import { Gantt } from '../components/Gantt'
import { TaskSheet } from '../components/TaskSheet'
import { SHEET_COLUMNS, SHEET_WIDTH } from '../components/sheetColumns'
import { durationDays, fromWireDate } from '../lib/format'
import { makeScale, ticks, monthTicks } from '../lib/timescale'
import { windowRange } from '../lib/virtualize'

const ROW_HEIGHT = 28
const PX_PER_DAY = 24
const HEADER_HEIGHT = 40

interface Props {
  client: ApiClient
  projectId: string
  userId: string
  onBack: () => void
}

export function ProjectView({ client, projectId, userId, onBack }: Props) {
  const [schedule, setSchedule] = useState<Schedule | null>(null)
  const [info, setInfo] = useState<ProjectInfo | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [selectedUid, setSelectedUid] = useState<number | null>(null)
  const [scrollTop, setScrollTop] = useState(0)
  const [viewportHeight, setViewportHeight] = useState(600)
  const [splitX, setSplitX] = useState(Math.min(SHEET_WIDTH + 12, 560))
  const scrollerRef = useRef<HTMLDivElement>(null)
  const ganttScrollRef = useRef<HTMLDivElement>(null)

  const refresh = useCallback(async () => {
    try {
      const [nextInfo, nextSchedule] = await Promise.all([client.getProject(projectId), client.schedule(projectId)])
      setInfo(nextInfo)
      setSchedule(nextSchedule)
      setError(null)
    } catch (cause) {
      setError(cause instanceof Error ? cause.message : String(cause))
    }
  }, [client, projectId])

  useEffect(() => {
    void refresh()
  }, [refresh])

  // Live refresh: readers follow check-ins; everyone follows lock changes.
  useEffect(() => {
    const unsubscribe = client.subscribe(projectId, (event) => {
      if (event.kind === 'checkin' || event.kind === 'checkout' || event.kind === 'lock-released') {
        void refresh()
      }
    })
    return unsubscribe
  }, [client, projectId, refresh])

  useEffect(() => {
    const element = scrollerRef.current
    if (element === null) return
    const observer = new ResizeObserver(() => setViewportHeight(element.clientHeight))
    observer.observe(element)
    return () => observer.disconnect()
  }, [])

  const holdsLock = info?.lock?.userId === userId
  const editable = holdsLock && (info?.role === 'editor' || info?.role === 'owner')

  const sendCommands = useCallback(
    async (commands: Command[]) => {
      try {
        const response = await client.commands(projectId, commands)
        setSchedule(response.schedule)
        setError(null)
      } catch (cause) {
        setError(cause instanceof Error ? cause.message : String(cause))
      }
    },
    [client, projectId],
  )

  async function checkout() {
    try {
      await client.checkout(projectId)
      await refresh()
    } catch (cause) {
      setError(cause instanceof Error ? cause.message : String(cause))
    }
  }

  async function checkin() {
    try {
      await client.unlock(projectId)
      await refresh()
    } catch (cause) {
      setError(cause instanceof Error ? cause.message : String(cause))
    }
  }

  const tasks = useMemo(() => schedule?.tasks ?? [], [schedule])
  const indexByUid = useMemo(() => new Map(tasks.map((task, index) => [task.uid, index])), [tasks])
  const rowByUid = useMemo(() => new Map(tasks.map((task) => [task.uid, task.row])), [tasks])
  const window_ = windowRange(scrollTop, viewportHeight, ROW_HEIGHT, tasks.length)

  const scale = useMemo(() => {
    if (schedule === null) return makeScale(new Date(), new Date(), PX_PER_DAY)
    const start = fromWireDate(schedule.project.start)
    const finish = schedule.project.finish !== null ? fromWireDate(schedule.project.finish) : start
    return makeScale(start, finish, PX_PER_DAY)
  }, [schedule])

  const selected = selectedUid !== null ? (tasks[indexByUid.get(selectedUid) ?? -1] ?? null) : null

  function addTask(milestone: boolean) {
    const name = milestone ? 'New milestone' : 'New task'
    const command: Command = selected?.summary
      ? { op: 'addTask', name, parentUid: selected.uid, milestone }
      : { op: 'addTask', name, milestone }
    void sendCommands([command])
  }

  return (
    <div className="project-view">
      <div className="toolbar">
        <button onClick={onBack}>← Projects</button>
        <h2>{schedule?.project.name ?? '…'}</h2>
        <span className="muted">
          {schedule !== null &&
            `v${schedule.version} · ${durationDays(schedule.project.totalWorkMinutes, schedule.project.minutesPerDay)} work`}
        </span>
        <span className="spacer" />
        {error !== null && <span className="error">{error}</span>}
        {info !== null && !holdsLock && info.lock !== null && (
          <span className="lock-banner">checked out by {info.lock.userId}</span>
        )}
        {editable ? (
          <>
            <button onClick={() => addTask(false)}>+ Task</button>
            <button onClick={() => addTask(true)}>+ Milestone</button>
            <button
              disabled={selected === null}
              onClick={() => selected !== null && void sendCommands([{ op: 'indentTask', uid: selected.uid }])}
            >
              Indent
            </button>
            <button
              disabled={selected === null}
              onClick={() => selected !== null && void sendCommands([{ op: 'outdentTask', uid: selected.uid }])}
            >
              Outdent
            </button>
            <button
              className="danger"
              disabled={selected === null}
              onClick={() => {
                if (selected !== null && window.confirm(`Delete '${selected.name}' and its subtasks?`)) {
                  setSelectedUid(null)
                  void sendCommands([{ op: 'removeTask', uid: selected.uid }])
                }
              }}
            >
              Delete
            </button>
            <button className="primary" onClick={() => void checkin()}>
              Check in
            </button>
          </>
        ) : (
          (info?.role === 'editor' || info?.role === 'owner') && (
            <button className="primary" onClick={() => void checkout()}>
              Checkout to edit
            </button>
          )
        )}
      </div>

      <div className="split" style={{ gridTemplateColumns: `${splitX}px 6px 1fr` }}>
        <div
          className="pane"
          ref={scrollerRef}
          onScroll={(event) => syncScroll(event.currentTarget, ganttScrollRef.current, setScrollTop)}
        >
          <div className="sheet-header" style={{ width: SHEET_WIDTH, height: HEADER_HEIGHT }}>
            {SHEET_COLUMNS.map((column) => (
              <span key={column.key} className="cell header" style={{ width: column.width }}>
                {column.label}
              </span>
            ))}
          </div>
          <TaskSheet
            tasks={tasks}
            rowByUid={rowByUid}
            minutesPerDay={schedule?.project.minutesPerDay ?? 480}
            rowHeight={ROW_HEIGHT}
            window_={window_}
            editable={editable}
            selectedUid={selectedUid}
            onSelect={setSelectedUid}
            onCommands={(commands) => void sendCommands(commands)}
          />
        </div>
        <Splitter onMove={(dx) => setSplitX((x) => Math.max(160, Math.min(1000, x + dx)))} />
        <div
          className="pane"
          ref={ganttScrollRef}
          onScroll={(event) => syncScroll(event.currentTarget, scrollerRef.current, setScrollTop)}
        >
          <div className="gantt-header" style={{ width: scale.width, height: HEADER_HEIGHT }}>
            <svg width={scale.width} height={HEADER_HEIGHT}>
              {monthTicks(scale).map((tick) => (
                <text key={'m' + tick.x} x={tick.x + 4} y={14} className="gantt-month">
                  {tick.label}
                </text>
              ))}
              {ticks(scale).map((tick) => (
                <g key={tick.x}>
                  <line
                    x1={tick.x}
                    x2={tick.x}
                    y1={22}
                    y2={HEADER_HEIGHT}
                    className={tick.major ? 'gantt-grid-major' : 'gantt-grid'}
                  />
                  <text x={tick.x + 3} y={35} className="gantt-day">
                    {tick.label}
                  </text>
                </g>
              ))}
            </svg>
          </div>
          <Gantt
            tasks={tasks}
            indexByUid={indexByUid}
            scale={scale}
            rowHeight={ROW_HEIGHT}
            window_={window_}
            editable={editable}
            selectedUid={selectedUid}
            onSelect={setSelectedUid}
            onCommands={(commands) => void sendCommands(commands)}
          />
        </div>
      </div>
    </div>
  )
}

/** Mirrors vertical scroll between the sheet and Gantt panes. */
function syncScroll(
  source: HTMLDivElement,
  other: HTMLDivElement | null,
  setScrollTop: (top: number) => void,
) {
  setScrollTop(source.scrollTop)
  if (other !== null && other.scrollTop !== source.scrollTop) {
    other.scrollTop = source.scrollTop
  }
}

function Splitter({ onMove }: { onMove: (dx: number) => void }) {
  const lastX = useRef(0)
  return (
    <div
      className="splitter"
      onPointerDown={(event) => {
        lastX.current = event.clientX
        event.currentTarget.setPointerCapture(event.pointerId)
      }}
      onPointerMove={(event) => {
        if (event.buttons !== 1) return
        onMove(event.clientX - lastX.current)
        lastX.current = event.clientX
      }}
      role="separator"
      aria-orientation="vertical"
    />
  )
}
