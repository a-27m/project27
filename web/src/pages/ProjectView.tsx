import { useCallback, useEffect, useMemo, useRef, useState } from 'react'
import type { ApiClient } from '../api/client'
import type { Command, ProjectInfo, Schedule } from '../api/types'
import { Gantt } from '../components/Gantt'
import { NetworkView } from '../components/NetworkView'
import { CalendarManager, CustomFieldsManager, RecurringTaskDialog } from '../components/Managers'
import { ProjectSettings } from '../components/ProjectSettings'
import { TableView } from '../components/TableView'
import { ResourcesView } from '../components/ResourcesView'
import { TaskInspector } from '../components/TaskInspector'
import { TaskSheet } from '../components/TaskSheet'
import { TimelineView } from '../components/TimelineView'
import { UsageView } from '../components/UsageView'
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
  const [viewMode, setViewMode] = useState<'gantt' | 'table' | 'network' | 'timeline' | 'usage' | 'resources'>('gantt')
  const [showSettings, setShowSettings] = useState(false)
  const [dialog, setDialog] = useState<'fields' | 'calendars' | 'recurring' | null>(null)
  const [undoStack, setUndoStack] = useState<Command[][]>([])
  const [redoStack, setRedoStack] = useState<Command[][]>([])
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

  const post = useCallback(
    async (commands: Command[], onInverse: (inverse: Command[] | null) => void) => {
      try {
        const response = await client.commands(projectId, commands)
        setSchedule(response.schedule)
        setError(null)
        onInverse(response.inverse)
      } catch (cause) {
        setError(cause instanceof Error ? cause.message : String(cause))
      }
    },
    [client, projectId],
  )

  const sendCommands = useCallback(
    (commands: Command[]) =>
      post(commands, (inverse) => {
        setRedoStack([])
        setUndoStack((stack) => (inverse === null ? [] : [...stack, inverse].slice(-50)))
      }),
    [post],
  )

  const undo = useCallback(() => {
    setUndoStack((stack) => {
      const batch = stack[stack.length - 1]
      if (batch !== undefined) {
        void post(batch, (inverse) => {
          if (inverse !== null) setRedoStack((redo) => [...redo, inverse])
        })
      }
      return stack.slice(0, -1)
    })
  }, [post])

  const redo = useCallback(() => {
    setRedoStack((stack) => {
      const batch = stack[stack.length - 1]
      if (batch !== undefined) {
        void post(batch, (inverse) => {
          if (inverse !== null) setUndoStack((undoBatches) => [...undoBatches, inverse])
        })
      }
      return stack.slice(0, -1)
    })
  }, [post])

  // Keyboard undo/redo while holding the lock.
  useEffect(() => {
    function onKeyDown(event: KeyboardEvent) {
      if (!(event.metaKey || event.ctrlKey) || event.key.toLowerCase() !== 'z') return
      const target = event.target as HTMLElement | null
      if (target !== null && (target.tagName === 'INPUT' || target.tagName === 'SELECT' || target.tagName === 'TEXTAREA')) return
      event.preventDefault()
      if (event.shiftKey) redo()
      else undo()
    }
    window.addEventListener('keydown', onKeyDown)
    return () => window.removeEventListener('keydown', onKeyDown)
  }, [undo, redo])

  async function checkout() {
    try {
      await client.checkout(projectId)
      setUndoStack([])
      setRedoStack([])
      await refresh()
    } catch (cause) {
      setError(cause instanceof Error ? cause.message : String(cause))
    }
  }

  async function checkin() {
    try {
      await client.unlock(projectId)
      setUndoStack([])
      setRedoStack([])
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
        <nav className="view-switch" aria-label="View">
          {(['gantt', 'table', 'network', 'timeline', 'usage', 'resources'] as const).map((mode) => (
            <button
              key={mode}
              className={viewMode === mode ? 'active' : ''}
              onClick={() => setViewMode(mode)}
            >
              {mode.charAt(0).toUpperCase() + mode.slice(1)}
            </button>
          ))}
        </nav>
        {editable && (
          <select
            className="report-menu"
            aria-label="Plan actions"
            value=""
            onChange={(event) => {
              const action = event.target.value
              event.target.value = ''
              if (action === 'baseline') void sendCommands([{ op: 'setBaseline' }])
              else if (action === 'clearBaseline') void sendCommands([{ op: 'clearBaseline' }])
              else if (action === 'level') void sendCommands([{ op: 'level' }])
              else if (action === 'clearLeveling') void sendCommands([{ op: 'clearLeveling' }])
              else if (action === 'reschedule') void sendCommands([{ op: 'reschedule' }])
            }}
          >
            <option value="">Plan…</option>
            <option value="baseline">Set baseline</option>
            <option value="clearBaseline">Clear baseline</option>
            <option value="level">Level resources</option>
            <option value="clearLeveling">Clear leveling</option>
            <option value="reschedule">Reschedule uncompleted work</option>
          </select>
        )}
        <button onClick={() => setShowSettings(true)}>Settings</button>
        <select
          className="report-menu"
          aria-label="Manage"
          value=""
          onChange={(event) => {
            const choice = event.target.value
            event.target.value = ''
            if (choice === 'fields' || choice === 'calendars' || choice === 'recurring') setDialog(choice)
          }}
        >
          <option value="">Manage…</option>
          <option value="fields">Custom fields</option>
          <option value="calendars">Calendars</option>
          <option value="recurring">Add recurring task</option>
        </select>
        <select
          className="report-menu"
          aria-label="Reports"
          value=""
          onChange={(event) => {
            const name = event.target.value
            if (name === '') return
            event.target.value = ''
            void client
              .reportHtml(projectId, name)
              .then((html) => {
                const url = URL.createObjectURL(new Blob([html], { type: 'text/html' }))
                window.open(url, '_blank', 'noopener')
                setTimeout(() => URL.revokeObjectURL(url), 60_000)
              })
              .catch((cause: unknown) => setError(cause instanceof Error ? cause.message : String(cause)))
          }}
        >
          <option value="">Reports…</option>
          <option value="overview">Project overview</option>
          <option value="critical">Critical tasks</option>
          <option value="late">Late tasks</option>
          <option value="resources">Resource overview</option>
          <option value="costs">Cost overview</option>
          <option value="upcoming">Upcoming tasks</option>
        </select>
        <span className="spacer" />
        {error !== null && <span className="error" role="alert">{error}</span>}
        {info !== null && !holdsLock && info.lock !== null && (
          <span className="lock-banner">checked out by {info.lock.userId}</span>
        )}
        {editable ? (
          <>
            <button onClick={undo} disabled={undoStack.length === 0} aria-label="Undo" title="Undo (Ctrl+Z)">
              ↶
            </button>
            <button onClick={redo} disabled={redoStack.length === 0} aria-label="Redo" title="Redo (Ctrl+Shift+Z)">
              ↷
            </button>
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

      {viewMode === 'network' && (
        <div className="view-body">
          <NetworkView
            tasks={tasks}
            minutesPerDay={schedule?.project.minutesPerDay ?? 480}
            selectedUid={selectedUid}
            onSelect={setSelectedUid}
          />
        </div>
      )}
      {viewMode === 'timeline' && schedule !== null && (
        <div className="view-body">
          <TimelineView
            tasks={tasks}
            projectStart={schedule.project.start}
            projectFinish={schedule.project.finish}
            selectedUid={selectedUid}
            onSelect={setSelectedUid}
          />
        </div>
      )}
      {viewMode === 'usage' && (
        <div className="view-body">
          <UsageView client={client} projectId={projectId} version={schedule?.version ?? 0} />
        </div>
      )}
      {viewMode === 'table' && (
        <div className="view-body">
          <TableView client={client} projectId={projectId} version={schedule?.version ?? 0} />
        </div>
      )}
      {viewMode === 'resources' && schedule !== null && (
        <div className="view-body">
          <ResourcesView
            project={schedule.project}
            editable={editable}
            onCommands={(commands) => void sendCommands(commands)}
          />
        </div>
      )}
      <div
        className="split"
        style={{ gridTemplateColumns: `${splitX}px 6px 1fr`, display: viewMode === 'gantt' ? undefined : 'none' }}
      >
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

      {dialog === 'fields' && schedule !== null && (
        <CustomFieldsManager
          project={schedule.project}
          editable={editable}
          onCommands={(commands) => void sendCommands(commands)}
          onClose={() => setDialog(null)}
        />
      )}
      {dialog === 'calendars' && schedule !== null && (
        <CalendarManager
          project={schedule.project}
          editable={editable}
          onCommands={(commands) => void sendCommands(commands)}
          onClose={() => setDialog(null)}
        />
      )}
      {dialog === 'recurring' && (
        <RecurringTaskDialog
          editable={editable}
          onCommands={(commands) => void sendCommands(commands)}
          onClose={() => setDialog(null)}
        />
      )}
      {showSettings && schedule !== null && (
        <ProjectSettings
          project={schedule.project}
          editable={editable}
          onCommands={(commands) => void sendCommands(commands)}
          onClose={() => setShowSettings(false)}
        />
      )}
      {selected !== null && schedule !== null && (
        <TaskInspector
          task={selected}
          project={schedule.project}
          tasks={tasks}
          editable={editable}
          client={client}
          projectId={projectId}
          onCommands={(commands) => void sendCommands(commands)}
          onClose={() => setSelectedUid(null)}
        />
      )}
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
