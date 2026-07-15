import { useCallback, useEffect, useMemo, useRef, useState } from 'react'
import type { ApiClient } from '../api/client'
import type { Command, ProjectInfo, Schedule } from '../api/types'
import { Gantt } from '../components/Gantt'
import { NetworkView } from '../components/NetworkView'
import { CalendarManager, CustomFieldsManager, HistoryDialog, RecurringTaskDialog } from '../components/Managers'
import { ProjectSettings } from '../components/ProjectSettings'
import { TableView } from '../components/TableView'
import { ResourcesView } from '../components/ResourcesView'
import { TaskInspector } from '../components/TaskInspector'
import { TaskSheet } from '../components/TaskSheet'
import { TimelineView } from '../components/TimelineView'
import { UsageView } from '../components/UsageView'
import { DropdownMenu, DropdownTrigger, type MenuGroup } from '../components/DropdownMenu'
import { Icon } from '../components/icons/Icon'
import {
  AVAILABLE_COLUMNS,
  columnsFor,
  loadColumnKeys,
  saveColumnKeys,
  sheetWidth,
} from '../components/sheetColumns'
import { buildDisplayRows, displayIndexByUid } from '../lib/displayRows'
import { dateOnly, durationDays, fromWireDate } from '../lib/format'
import { pruneNested, rangeBetween, siblingMove } from '../lib/outline'
import { makeScale, ticks, monthTicks } from '../lib/timescale'
import { useOutsideClose } from '../lib/useOutsideClose'
import { windowRange } from '../lib/virtualize'

const ROW_HEIGHT = 28
const HEADER_HEIGHT = 40
const ZOOM_LEVELS = [6, 10, 16, 24, 36, 56] as const

interface Props {
  client: ApiClient
  projectId: string
  userId: string
  userDisplayName: string
  dark: boolean
  onToggleTheme: () => void
  onSignOut: () => void
  onBack: () => void
}

type ViewMode = 'gantt' | 'table' | 'network' | 'timeline' | 'usage' | 'resources'
type Dialog = 'fields' | 'calendars' | 'recurring' | 'history' | 'columns' | 'settings' | null

export function ProjectView({ client, projectId, userId, userDisplayName, dark, onToggleTheme, onSignOut, onBack }: Props) {
  const [schedule, setSchedule] = useState<Schedule | null>(null)
  const [info, setInfo] = useState<ProjectInfo | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [selectedUids, setSelectedUids] = useState<ReadonlySet<number>>(new Set())
  const [anchorUid, setAnchorUid] = useState<number | null>(null)
  const [scrollTop, setScrollTop] = useState(0)
  const [viewportHeight, setViewportHeight] = useState(600)
  const [splitX, setSplitX] = useState(620)
  const [viewMode, setViewMode] = useState<ViewMode>('gantt')
  const [dialog, setDialog] = useState<Dialog>(null)
  const [zoomIndex, setZoomIndex] = useState(3) // 24 px/day
  const [columnKeys, setColumnKeys] = useState<string[]>(loadColumnKeys)
  const [undoStack, setUndoStack] = useState<Command[][]>([])
  const [redoStack, setRedoStack] = useState<Command[][]>([])
  const [inspectorCollapsed, setInspectorCollapsed] = useState(false)
  const [checkinOpen, setCheckinOpen] = useState(false)
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

  async function checkin(label?: string) {
    try {
      if (label !== undefined && label.trim() !== '') {
        await client.labelVersion(projectId, label.trim())
      }
      await client.unlock(projectId)
      setUndoStack([])
      setRedoStack([])
      setCheckinOpen(false)
      await refresh()
    } catch (cause) {
      setError(cause instanceof Error ? cause.message : String(cause))
    }
  }

  const tasks = useMemo(() => schedule?.tasks ?? [], [schedule])
  const indexByUid = useMemo(() => new Map(tasks.map((task, index) => [task.uid, index])), [tasks])
  const rowByUid = useMemo(() => new Map(tasks.map((task) => [task.uid, task.row])), [tasks])
  // Sheet/Gantt panes render cosmetic spaceAfter as extra rows; displayByUid (not
  // indexByUid) is the row-position map for anything that must align with them.
  const displayRows = useMemo(() => buildDisplayRows(tasks), [tasks])
  const displayByUid = useMemo(() => displayIndexByUid(displayRows), [displayRows])
  const window_ = windowRange(scrollTop, viewportHeight, ROW_HEIGHT, displayRows.length)
  const columns = useMemo(() => columnsFor(columnKeys), [columnKeys])
  const columnContext = useMemo(
    () => ({ minutesPerDay: schedule?.project.minutesPerDay ?? 480, rowByUid }),
    [schedule, rowByUid],
  )

  const pxPerDay = ZOOM_LEVELS[zoomIndex]
  const scale = useMemo(() => {
    if (schedule === null) return makeScale(new Date(), new Date(), pxPerDay)
    const start = fromWireDate(schedule.project.start)
    const finish = schedule.project.finish !== null ? fromWireDate(schedule.project.finish) : start
    return makeScale(start, finish, pxPerDay)
  }, [schedule, pxPerDay])

  // Selection: click = single; Ctrl/Cmd = toggle; Shift = range from the anchor.
  const selectTask = useCallback(
    (uid: number | null, modifiers?: { toggle: boolean; range: boolean }) => {
      if (uid === null) {
        setSelectedUids(new Set())
        setAnchorUid(null)
        return
      }
      setSelectedUids((current) => {
        if (modifiers?.range && anchorUid !== null) {
          return new Set(rangeBetween(tasks, anchorUid, uid))
        }
        if (modifiers?.toggle) {
          const next = new Set(current)
          if (next.has(uid)) next.delete(uid)
          else next.add(uid)
          return next
        }
        return new Set([uid])
      })
      if (!modifiers?.range) setAnchorUid(uid)
      setInspectorCollapsed(false)
    },
    [anchorUid, tasks],
  )

  const selected = selectedUids.size === 1 ? (tasks[indexByUid.get([...selectedUids][0]) ?? -1] ?? null) : null
  const selectionRoots = useMemo(() => pruneNested(tasks, selectedUids), [tasks, selectedUids])
  const selectedLeaves = useMemo(
    () => [...selectedUids].filter((uid) => tasks[indexByUid.get(uid) ?? -1]?.summary === false),
    [selectedUids, tasks, indexByUid],
  )

  function addTask(kind: 'task' | 'milestone') {
    const command: Command = {
      op: 'addTask',
      name: kind === 'milestone' ? 'New milestone' : 'New task',
      ...(kind === 'milestone' ? { milestone: true } : {}),
      ...(selected?.summary ? { parentUid: selected.uid } : {}),
    }
    void sendCommands([command])
  }

  function setSpaceAfter(delta: number): void {
    const commands: Command[] = [...selectedUids].flatMap((uid) => {
      const task = tasks[indexByUid.get(uid) ?? -1]
      if (task === undefined) return []
      const next = Math.min(20, Math.max(0, task.spaceAfter + delta))
      return next === task.spaceAfter ? [] : [{ op: 'setTask', uid, spaceAfter: next }]
    })
    if (commands.length > 0) void sendCommands(commands)
  }

  function forSelection(op: 'indentTask' | 'outdentTask'): void {
    if (selectionRoots.length === 0) return
    void sendCommands(selectionRoots.map((uid) => ({ op, uid })))
  }

  function deleteSelection(): void {
    if (selectionRoots.length === 0) return
    const label = selectionRoots.length === 1
      ? `'${tasks[indexByUid.get(selectionRoots[0]) ?? -1]?.name ?? ''}'`
      : `${selectionRoots.length} tasks`
    if (!window.confirm(`Delete ${label} (including subtasks)?`)) return
    selectTask(null)
    void sendCommands(selectionRoots.map((uid) => ({ op: 'removeTask', uid })))
  }

  function moveSelection(direction: 'up' | 'down'): void {
    if (selected === null) return
    const move = siblingMove(tasks, selected.uid, direction)
    if (move !== null) void sendCommands([{ op: 'moveTask', ...move }])
  }

  function setPercent(percent: number): void {
    if (selectedLeaves.length === 0) return
    void sendCommands(selectedLeaves.map((uid) => ({ op: 'setTask', uid, percentComplete: percent })))
  }

  function assignToSelection(resource: string): void {
    const targets = selectedLeaves.filter(
      (uid) => !tasks[indexByUid.get(uid) ?? -1]?.assignments.some((a) => a.resource === resource),
    )
    if (targets.length > 0) {
      void sendCommands(targets.map((uid) => ({ op: 'assign', uid, resource })))
    }
  }

  // Keyboard: navigation and editing shortcuts (outside form fields).
  useEffect(() => {
    function onKeyDown(event: KeyboardEvent) {
      const target = event.target as HTMLElement | null
      if (target !== null && ['INPUT', 'SELECT', 'TEXTAREA'].includes(target.tagName)) return

      const meta = event.metaKey || event.ctrlKey
      if (meta && event.key.toLowerCase() === 'z') {
        event.preventDefault()
        if (event.shiftKey) redo()
        else undo()
        return
      }

      if (event.key === 'ArrowUp' || event.key === 'ArrowDown') {
        event.preventDefault()
        if (meta && editable) {
          moveSelection(event.key === 'ArrowUp' ? 'up' : 'down')
          return
        }
        const currentIndex = anchorUid !== null ? (indexByUid.get(anchorUid) ?? -1) : -1
        const nextIndex = event.key === 'ArrowUp'
          ? (currentIndex <= 0 ? 0 : currentIndex - 1)
          : Math.min(tasks.length - 1, currentIndex + 1)
        const next = tasks[nextIndex]
        if (next !== undefined) selectTask(next.uid, { toggle: false, range: event.shiftKey })
        return
      }

      if (editable && event.altKey && event.shiftKey && (event.key === 'ArrowRight' || event.key === 'ArrowLeft')) {
        event.preventDefault()
        forSelection(event.key === 'ArrowRight' ? 'indentTask' : 'outdentTask')
        return
      }

      if (editable && (event.key === 'Delete' || (event.key === 'Backspace' && meta))) {
        event.preventDefault()
        deleteSelection()
        return
      }

      if (event.key === 'Escape') {
        selectTask(null)
      }
    }
    window.addEventListener('keydown', onKeyDown)
    return () => window.removeEventListener('keydown', onKeyDown)
  })

  function download(kind: 'p27' | 'mspdi') {
    const path = kind === 'p27' ? `/api/projects/${projectId}/file` : `/api/projects/${projectId}/export/mspdi`
    void client
      .download(path, kind === 'p27' ? 'project.p27' : 'project.xml')
      .catch((cause: unknown) => setError(cause instanceof Error ? cause.message : String(cause)))
  }

  function openReport(name: string) {
    void client
      .reportHtml(projectId, name)
      .then((html) => {
        const url = URL.createObjectURL(new Blob([html], { type: 'text/html' }))
        window.open(url, '_blank', 'noopener')
        setTimeout(() => URL.revokeObjectURL(url), 60_000)
      })
      .catch((cause: unknown) => setError(cause instanceof Error ? cause.message : String(cause)))
  }

  const gridWidth = sheetWidth(columns)

  /** Rare/global actions that don't belong on the hot path: grouped under the ⋯ menu.
   *  Available to readers too (D6, readers never blocked) — Reports/Export/Manage all view state. */
  const overflowGroups: MenuGroup[] = [
    {
      heading: 'MANAGE',
      items: [
        { label: 'Custom fields', onClick: () => setDialog('fields') },
        { label: 'Calendars', onClick: () => setDialog('calendars') },
        { label: 'Add recurring task', onClick: () => setDialog('recurring') },
        { label: 'Project settings', onClick: () => setDialog('settings') },
        { label: 'Version history', onClick: () => setDialog('history') },
      ],
    },
    {
      heading: 'REPORTS',
      items: [
        { label: 'Project overview', onClick: () => openReport('overview') },
        { label: 'Critical tasks', onClick: () => openReport('critical') },
        { label: 'Late tasks', onClick: () => openReport('late') },
        { label: 'Resource overview', onClick: () => openReport('resources') },
        { label: 'Cost overview', onClick: () => openReport('costs') },
        { label: 'Upcoming tasks', onClick: () => openReport('upcoming') },
      ],
    },
    {
      heading: 'EXPORT',
      items: [
        { label: 'Project file (.p27)', onClick: () => download('p27') },
        { label: 'MS Project XML', onClick: () => download('mspdi') },
      ],
    },
  ]

  return (
    <div className="project-view">
      {/* Top bar: workspace, identity, view switch, session */}
      <div className="toolbar">
        <DropdownMenu
          ariaLabel="Workspace menu"
          trigger={({ open, toggle }) => (
            <button className="icon-btn" onClick={toggle} title="Workspace menu" aria-label="Workspace menu" aria-haspopup="menu" aria-expanded={open}>
              <Icon name="Menu" size={16} />
            </button>
          )}
          groups={[{ heading: 'WORKSPACE', items: [{ label: 'All projects', onClick: onBack }] }]}
        />
        <button
          className="project-btn"
          onClick={() => setDialog('settings')}
          title="Project info & settings"
        >
          {schedule?.project.name ?? '…'}
        </button>
        {schedule !== null && (
          <button className="version-chip" onClick={() => setDialog('history')} title="Version history">
            v{schedule.version}
          </button>
        )}
        {schedule !== null && (
          <span className="status-chip" title="Status date — the as-of date for tracking & earned value">
            STATUS {schedule.project.statusDate !== null ? dateOnly(schedule.project.statusDate) : '—'}
          </span>
        )}
        <span className="muted">
          {schedule !== null && `${durationDays(schedule.project.totalWorkMinutes, schedule.project.minutesPerDay)} work`}
        </span>
        <nav className="view-switch" aria-label="View">
          {(['gantt', 'table', 'network', 'timeline', 'usage', 'resources'] as const).map((mode) => (
            <button key={mode} className={viewMode === mode ? 'active' : ''} onClick={() => setViewMode(mode)}>
              {mode.charAt(0).toUpperCase() + mode.slice(1)}
            </button>
          ))}
        </nav>
        <span className="spacer" />
        {info !== null && !holdsLock && info.lock !== null && (
          <span className="lock-banner">checked out by {info.lock.displayName}</span>
        )}
        {editable ? (
          <span className="dropdown checkin-split">
            <button className="primary checkin-main" onClick={() => void checkin()} title="Check in — releases your lock">
              Check in
            </button>
            <button
              className="primary checkin-caret"
              onClick={() => setCheckinOpen((o) => !o)}
              title="Check in with a comment…"
              aria-haspopup="dialog"
              aria-expanded={checkinOpen}
            >
              <Icon name="CaretDown" size={12} />
            </button>
            {checkinOpen && <CheckinPopover version={schedule?.version ?? 0} onCheckin={(comment) => void checkin(comment)} onClose={() => setCheckinOpen(false)} />}
          </span>
        ) : (
          (info?.role === 'editor' || info?.role === 'owner') && (
            <button className="primary" onClick={() => void checkout()}>
              Checkout to edit
            </button>
          )
        )}
        <DropdownMenu
          ariaLabel="Account"
          align="right"
          trigger={({ open, toggle }) => (
            <button className="avatar-btn" onClick={toggle} title={`${userDisplayName} — account`} aria-haspopup="menu" aria-expanded={open}>
              <span className="avatar-circle">{userDisplayName.charAt(0).toUpperCase()}</span>
              {userDisplayName}
              <Icon name="CaretDown" size={12} />
            </button>
          )}
          groups={[
            { items: [{ label: dark ? 'Switch to light theme' : 'Switch to dark theme', onClick: onToggleTheme }] },
            { items: [{ label: 'Sign out', onClick: onSignOut }] },
          ]}
        />
      </div>

      {/* Context action bar: only ever shows verbs legal for the current selection */}
      <div className="action-bar">
        {editable && (
          <>
            <span className="action-group">
              <button className="icon-btn" onClick={undo} disabled={undoStack.length === 0} aria-label="Undo" title="Undo (Ctrl+Z)">
                ↶
              </button>
              <button className="icon-btn" onClick={redo} disabled={redoStack.length === 0} aria-label="Redo" title="Redo (Ctrl+Shift+Z)">
                ↷
              </button>
            </span>
            <span className="action-group">
              <button onClick={() => addTask('task')} title="Add task (N)">
                <Icon name="Add" size={12} /> Task
              </button>
              <button onClick={() => addTask('milestone')}>+ Milestone</button>
            </span>
            {selectionRoots.length > 0 && (
              <span className="action-group" role="group" aria-label="Structure">
                <button className="icon-btn" onClick={() => setSpaceAfter(1)} title="Add space below the selected row(s)">
                  Space +
                </button>
                <button className="icon-btn" onClick={() => setSpaceAfter(-1)} title="Remove space below the selected row(s)">
                  Space −
                </button>
                <button className="icon-btn" onClick={() => forSelection('indentTask')} title="Indent (Alt+Shift+→)" aria-label="Indent">
                  <Icon name="ArrowRight" size={14} />
                </button>
                <button className="icon-btn" onClick={() => forSelection('outdentTask')} title="Outdent (Alt+Shift+←)" aria-label="Outdent">
                  <Icon name="ArrowLeft" size={14} />
                </button>
                <button className="icon-btn" disabled={selected === null} onClick={() => moveSelection('up')} title="Move up (Ctrl+↑)" aria-label="Move up">
                  <Icon name="ArrowUp" size={14} />
                </button>
                <button className="icon-btn" disabled={selected === null} onClick={() => moveSelection('down')} title="Move down (Ctrl+↓)" aria-label="Move down">
                  <Icon name="ArrowDown" size={14} />
                </button>
                <button className="icon-btn danger" onClick={deleteSelection} title="Delete (Del)" aria-label="Delete">
                  <Icon name="Close" size={14} />
                </button>
              </span>
            )}
            {selectedLeaves.length > 0 && (
              <span className="action-group" role="group" aria-label="Progress & assignment">
                {[0, 25, 50, 75, 100].map((percent) => (
                  <button key={percent} className="icon-btn" onClick={() => setPercent(percent)}>
                    {percent}%
                  </button>
                ))}
                {(schedule?.project.resources.length ?? 0) > 0 && (
                  <select
                    className="menu"
                    aria-label="Assign resource to selection"
                    value=""
                    onChange={(event) => {
                      const resource = event.target.value
                      event.target.value = ''
                      if (resource !== '') assignToSelection(resource)
                    }}
                  >
                    <option value="">Assign to {selectedLeaves.length} selected…</option>
                    {schedule?.project.resources.map((resource) => (
                      <option key={resource.uid} value={resource.name}>
                        {resource.name}
                      </option>
                    ))}
                  </select>
                )}
              </span>
            )}
          </>
        )}
        <span className="spacer" />
        {error !== null && (
          <span className="error" role="alert">
            {error}
          </span>
        )}
        <span className="action-group">
          {editable && (
            <>
              <DropdownMenu
                ariaLabel="Baseline"
                trigger={({ open, toggle }) => <DropdownTrigger label="Baseline" open={open} onClick={toggle} />}
                groups={[
                  {
                    items: [
                      { label: 'Set baseline', onClick: () => void sendCommands([{ op: 'setBaseline' }]) },
                      { label: 'Clear baseline', onClick: () => void sendCommands([{ op: 'clearBaseline' }]) },
                    ],
                  },
                ]}
              />
              <DropdownMenu
                ariaLabel="Level"
                trigger={({ open, toggle }) => <DropdownTrigger label="Level" open={open} onClick={toggle} />}
                groups={[
                  {
                    items: [
                      { label: 'Level resources', onClick: () => void sendCommands([{ op: 'level' }]) },
                      { label: 'Clear leveling', onClick: () => void sendCommands([{ op: 'clearLeveling' }]) },
                    ],
                  },
                ]}
              />
              <button className="icon-btn" onClick={() => void sendCommands([{ op: 'reschedule' }])} title="Push uncompleted work past the status date">
                Reschedule
              </button>
            </>
          )}
          {/* Rare/global actions, incl. read-only ones (Reports, Download): available to readers too — D6, readers never blocked. */}
          <DropdownMenu
            ariaLabel="More project actions"
            align="right"
            trigger={({ open, toggle }) => (
              <button className="icon-btn" onClick={toggle} title="More project actions" aria-haspopup="menu" aria-expanded={open}>
                <Icon name="OverflowMenuHorizontal" size={16} />
              </button>
            )}
            groups={overflowGroups}
          />
        </span>
        {viewMode === 'gantt' && (
          <span className="action-group" role="group" aria-label="Zoom" style={{ borderRight: 'none' }}>
            <button className="icon-btn" onClick={() => setZoomIndex((z) => Math.max(0, z - 1))} disabled={zoomIndex === 0} title="Zoom out" aria-label="Zoom out">
              <Icon name="Subtract" size={14} />
            </button>
            <span className="muted mono">{pxPerDay}px/d</span>
            <button
              className="icon-btn"
              onClick={() => setZoomIndex((z) => Math.min(ZOOM_LEVELS.length - 1, z + 1))}
              disabled={zoomIndex === ZOOM_LEVELS.length - 1}
              title="Zoom in"
              aria-label="Zoom in"
            >
              <Icon name="Add" size={14} />
            </button>
            <button className="icon-btn" onClick={() => setDialog('columns')}>Columns</button>
          </span>
        )}
      </div>

      {viewMode === 'network' && (
        <div className="view-body">
          <NetworkView
            tasks={tasks}
            minutesPerDay={schedule?.project.minutesPerDay ?? 480}
            selectedUid={selected?.uid ?? null}
            onSelect={(uid) => selectTask(uid)}
          />
        </div>
      )}
      {viewMode === 'timeline' && schedule !== null && (
        <div className="view-body">
          <TimelineView
            tasks={tasks}
            projectStart={schedule.project.start}
            projectFinish={schedule.project.finish}
            selectedUid={selected?.uid ?? null}
            onSelect={(uid) => selectTask(uid)}
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
          <div className="sheet-header" style={{ width: gridWidth, height: HEADER_HEIGHT }}>
            {columns.map((column) => (
              <span key={column.key} className="cell header" style={{ width: column.width }}>
                {column.label}
              </span>
            ))}
          </div>
          <TaskSheet
            displayRows={displayRows}
            columns={columns}
            context={columnContext}
            rowHeight={ROW_HEIGHT}
            window_={window_}
            editable={editable}
            selectedUids={selectedUids}
            onSelect={(uid, modifiers) => selectTask(uid, modifiers)}
            onCommands={(commands) => void sendCommands(commands)}
          />
        </div>
        <Splitter onMove={(dx) => setSplitX((x) => Math.max(160, Math.min(1100, x + dx)))} />
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
            displayRows={displayRows}
            indexByUid={displayByUid}
            scale={scale}
            rowHeight={ROW_HEIGHT}
            window_={window_}
            editable={editable}
            selectedUids={selectedUids}
            statusDate={schedule?.project.statusDate}
            onSelect={(uid) => selectTask(uid)}
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
      {dialog === 'history' && (
        <HistoryDialog
          client={client}
          projectId={projectId}
          editable={editable}
          onReverted={() => void refresh()}
          onClose={() => setDialog(null)}
        />
      )}
      {dialog === 'columns' && (
        <ColumnsDialog
          columnKeys={columnKeys}
          onChange={(keys) => {
            setColumnKeys(keys)
            saveColumnKeys(keys)
          }}
          onClose={() => setDialog(null)}
        />
      )}
      {dialog === 'settings' && schedule !== null && (
        <ProjectSettings
          project={schedule.project}
          editable={editable}
          onCommands={(commands) => void sendCommands(commands)}
          onClose={() => setDialog(null)}
        />
      )}
      {selected !== null && schedule !== null && !inspectorCollapsed && (
        <TaskInspector
          task={selected}
          project={schedule.project}
          tasks={tasks}
          editable={editable}
          client={client}
          projectId={projectId}
          onCommands={(commands) => void sendCommands(commands)}
          onClose={() => selectTask(null)}
          onCollapse={() => setInspectorCollapsed(true)}
        />
      )}
      {selected !== null && inspectorCollapsed && (
        <button
          className="inspector-tab"
          onClick={() => setInspectorCollapsed(false)}
          title="Open inspector"
          aria-label="Open inspector"
        >
          <Icon name="ChevronLeft" size={14} />
          <span>INSPECTOR</span>
        </button>
      )}
    </div>
  )
}

/** Check-in-with-comment popover, opened from the check-in split button's caret. */
function CheckinPopover({ version, onCheckin, onClose }: { version: number; onCheckin: (comment: string) => void; onClose: () => void }) {
  const [comment, setComment] = useState('')
  const ref = useRef<HTMLDivElement>(null)
  useOutsideClose(ref, true, onClose)
  return (
    <div className="dropdown-panel align-right checkin-popover" ref={ref} role="dialog" aria-label="Check in with a comment">
      <span className="dropdown-heading">CHECK-IN COMMENT · v{version + 1}</span>
      <textarea
        autoFocus
        value={comment}
        onChange={(event) => setComment(event.target.value)}
        placeholder="What changed in this revision?"
      />
      <div className="checkin-actions">
        <button onClick={onClose}>Cancel</button>
        <button className="primary" onClick={() => onCheckin(comment)}>
          Check in
        </button>
      </div>
    </div>
  )
}

function ColumnsDialog({
  columnKeys,
  onChange,
  onClose,
}: {
  columnKeys: string[]
  onChange: (keys: string[]) => void
  onClose: () => void
}) {
  return (
    <div
      className="modal-backdrop"
      role="presentation"
      onClick={onClose}
      onKeyDown={(event) => {
        if (event.key === 'Escape') onClose()
      }}
    >
      <div
        className="modal"
        role="dialog"
        aria-modal="true"
        aria-label="Sheet columns"
        tabIndex={-1}
        ref={(element) => element?.focus()}
        onClick={(event) => event.stopPropagation()}
      >
        <h3>Sheet columns</h3>
        <div className="checks column-checks">
          {AVAILABLE_COLUMNS.map((column) => (
            <label key={column.key}>
              <input
                type="checkbox"
                checked={columnKeys.includes(column.key)}
                disabled={column.key === 'name'}
                onChange={(event) =>
                  onChange(
                    event.target.checked
                      ? AVAILABLE_COLUMNS.filter((c) => columnKeys.includes(c.key) || c.key === column.key).map((c) => c.key)
                      : columnKeys.filter((key) => key !== column.key),
                  )
                }
              />
              {column.key === 'mode' ? 'Mode (manual ✋)' : column.label}
            </label>
          ))}
        </div>
        <div className="modal-actions">
          <button onClick={onClose}>Close</button>
        </div>
      </div>
    </div>
  )
}

/** Mirrors vertical scroll between the sheet and Gantt panes. */
function syncScroll(source: HTMLDivElement, other: HTMLDivElement | null, setScrollTop: (top: number) => void) {
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
