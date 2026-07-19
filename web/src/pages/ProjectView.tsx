import { useCallback, useEffect, useMemo, useRef, useState } from 'react'
import { createPortal } from 'react-dom'
import type { ApiClient } from '../api/client'
import type { Command, FieldSummary, ProjectInfo, Schedule } from '../api/types'
import { Gantt } from '../components/Gantt'
import { MultiTaskInspector } from '../components/MultiTaskInspector'
import { NetworkView } from '../components/NetworkView'
import { ProjectInspector } from '../components/ProjectInspector'
import { CalendarManager, CustomFieldsManager, HistoryDialog, RecurringTaskDialog } from '../components/Managers'
import { TableView } from '../components/TableView'
import { ResourcesView } from '../components/ResourcesView'
import { DEFAULT_RESOURCE_COLUMN_KEYS, RESOURCE_COLUMNS } from '../components/resourceColumns'
import { SegmentedPercent } from '../components/SegmentedPercent'
import { TaskInspector } from '../components/TaskInspector'
import { TaskSheet } from '../components/TaskSheet'
import { TimelineView } from '../components/TimelineView'
import { UsageView } from '../components/UsageView'
import { DropdownMenu, DropdownTrigger, type MenuGroup } from '../components/DropdownMenu'
import { Icon } from '../components/icons/Icon'
import { ColumnsDialog } from '../components/ColumnsDialog'
import { DEFAULT_COLUMN_KEYS, columnsFor, columnsForProject, sheetWidth } from '../components/sheetColumns'
import { TABLE_DEFAULT_FIELDS } from '../components/tableColumns'
import { isCollapsible, visibleTasks as visibleTasksOf } from '../lib/collapse'
import { loadCollapsed, saveCollapsed } from '../lib/collapseStore'
import { buildDisplayRows, displayIndexByUid } from '../lib/displayRows'
import { fromWireDate } from '../lib/format'
import { pruneNested, rangeBetween, siblingMove } from '../lib/outline'
import { dropTarget } from '../lib/rowDrag'
import { useColumnPreferences } from '../lib/preferences'
import { makeScale, ticks, monthTicks } from '../lib/timescale'
import { useOutsideClose } from '../lib/useOutsideClose'
import { windowRange } from '../lib/virtualize'

const ROW_HEIGHT = 26
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
type Dialog = 'fields' | 'calendars' | 'recurring' | 'history' | 'columns' | 'level' | null

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
  const [showBaselineGhosts, setShowBaselineGhosts] = useState(true)
  const prefs = useColumnPreferences(client, projectId)
  // Browser-local: which summary tasks are folded. Never sent to the server.
  const [collapsed, setCollapsed] = useState<Set<number>>(() => loadCollapsed(projectId))
  const [tableSubview, setTableSubview] = useState<string>('entry')
  const [fieldCatalog, setFieldCatalog] = useState<FieldSummary[]>([])
  const [undoStack, setUndoStack] = useState<Command[][]>([])
  const [redoStack, setRedoStack] = useState<Command[][]>([])
  const [inspectorCollapsed, setInspectorCollapsed] = useState(false)
  const [inspectorScope, setInspectorScope] = useState<'task' | 'project'>('task')
  const [checkinOpen, setCheckinOpen] = useState(false)
  const checkinAnchorRef = useRef<HTMLSpanElement>(null)
  const [imageTag, setImageTag] = useState<string | null>(null)
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

  useEffect(() => {
    client.fields(projectId).then(setFieldCatalog).catch(() => setFieldCatalog([]))
  }, [client, projectId])

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

  useEffect(() => {
    client
      .version()
      .then((info) => setImageTag(info.imageTag))
      .catch(() => {
        /* cosmetic only; the ☰ menu's help link just falls back to "latest" */
      })
  }, [client])

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
  // Collapse is a view concern: descendants of collapsed summaries are dropped before the
  // panes render, so both the sheet and the SVG Gantt stay row-aligned for free. Every
  // *mutation* still computes against the full `tasks` list (see the drag drop handler).
  const visible = useMemo(() => visibleTasksOf(tasks, collapsed), [tasks, collapsed])
  const visibleIndexByUid = useMemo(
    () => new Map(visible.map((task, index) => [task.uid, index])),
    [visible],
  )
  // Sheet/Gantt panes render cosmetic spaceAfter as extra rows; displayByUid (not
  // indexByUid) is the row-position map for anything that must align with them.
  const displayRows = useMemo(() => buildDisplayRows(visible), [visible])
  const displayByUid = useMemo(() => displayIndexByUid(displayRows), [displayRows])
  const window_ = windowRange(scrollTop, viewportHeight, ROW_HEIGHT, displayRows.length)

  // Reset folded state when switching projects; persist it (browser-local) as it changes.
  useEffect(() => setCollapsed(loadCollapsed(projectId)), [projectId])
  useEffect(() => saveCollapsed(projectId, collapsed), [projectId, collapsed])
  const availableColumns = useMemo(
    () => (schedule === null ? [] : columnsForProject(schedule.project)),
    [schedule],
  )
  const ganttColumnKeys = useMemo(() => prefs.preferences.gantt ?? [...DEFAULT_COLUMN_KEYS], [prefs.preferences.gantt])
  const columns = useMemo(() => columnsFor(ganttColumnKeys, availableColumns), [ganttColumnKeys, availableColumns])
  const resourcesColumnKeys = prefs.preferences.resources ?? DEFAULT_RESOURCE_COLUMN_KEYS
  const storedTableColumnKeys = prefs.preferences.table?.[tableSubview] ?? []
  const tableColumnKeys =
    storedTableColumnKeys.length > 0 ? storedTableColumnKeys : (TABLE_DEFAULT_FIELDS[tableSubview] ?? [])
  const columnContext = useMemo(
    () => ({ minutesPerDay: schedule?.project.minutesPerDay ?? 480, rowByUid }),
    [schedule, rowByUid],
  )

  // "Columns…" lives in the ⋯ overflow menu (same as Gantt) for every view that has columns.
  const columnsDialogConfig =
    viewMode === 'gantt'
      ? {
          title: 'Sheet columns',
          options: availableColumns.map((c) => ({ key: c.key, label: c.key === 'mode' ? 'Mode (manual ✋)' : c.label, group: c.group })),
          selectedKeys: ganttColumnKeys,
          mandatoryKey: 'name',
          defaultKeys: [...DEFAULT_COLUMN_KEYS],
          onChange: prefs.setGanttColumns,
        }
      : viewMode === 'resources'
        ? {
            title: 'Resource columns',
            options: RESOURCE_COLUMNS,
            selectedKeys: resourcesColumnKeys,
            mandatoryKey: 'name',
            defaultKeys: DEFAULT_RESOURCE_COLUMN_KEYS,
            onChange: prefs.setResourcesColumns,
          }
        : viewMode === 'table'
          ? {
              title: `${tableSubview} columns`,
              options: fieldCatalog.map((f) => ({ key: f.key, label: f.caption, group: f.group })),
              selectedKeys: tableColumnKeys,
              mandatoryKey: undefined,
              defaultKeys: TABLE_DEFAULT_FIELDS[tableSubview] ?? [],
              onChange: (keys: string[]) => prefs.setTableColumns(tableSubview, keys),
            }
          : null

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
          // Range over the visible order so a shift-select spanning a folded summary
          // doesn't silently pull its hidden descendants into the selection.
          return new Set(rangeBetween(visible, anchorUid, uid))
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
      setInspectorScope('task')
      setInspectorCollapsed(false)
    },
    [anchorUid, visible],
  )

  const selected = selectedUids.size === 1 ? (tasks[indexByUid.get([...selectedUids][0]) ?? -1] ?? null) : null
  const selectionRoots = useMemo(() => pruneNested(tasks, selectedUids), [tasks, selectedUids])
  const selectedLeaves = useMemo(
    () => [...selectedUids].filter((uid) => tasks[indexByUid.get(uid) ?? -1]?.summary === false),
    [selectedUids, tasks, indexByUid],
  )
  const selectedTasks = useMemo(
    () => [...selectedUids].map((uid) => tasks[indexByUid.get(uid) ?? -1]).filter((task) => task !== undefined),
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

  // Fold/unfold a summary. On fold, if the keyboard anchor falls inside the now-hidden
  // subtree, move it up to the summary so navigation stays on a visible row.
  const toggleCollapse = useCallback(
    (uid: number, force?: 'collapse' | 'expand') => {
      if (!isCollapsible(tasks, uid)) return
      let didCollapse = false
      setCollapsed((current) => {
        const next = new Set(current)
        const shouldCollapse = force === undefined ? !next.has(uid) : force === 'collapse'
        didCollapse = shouldCollapse
        if (shouldCollapse) next.add(uid)
        else next.delete(uid)
        return next
      })
      if (!didCollapse) return
      const start = tasks.findIndex((t) => t.uid === uid)
      const level = tasks[start].outlineLevel
      setAnchorUid((anchor) => {
        if (anchor === null) return anchor
        const at = tasks.findIndex((t) => t.uid === anchor)
        const inSubtree = at > start && tasks[at].outlineLevel > level &&
          tasks.slice(start + 1, at).every((t) => t.outlineLevel > level)
        return inSubtree ? uid : anchor
      })
    },
    [tasks],
  )

  // Drag-drop reorder/reparent: resolve against the full model, emit moveTask.
  const dropRow = useCallback(
    (draggedUid: number, aboveUid: number | null, indentHint: number): void => {
      const move = dropTarget(tasks, draggedUid, aboveUid, indentHint, collapsed)
      if (move !== null) void sendCommands([{ op: 'moveTask', uid: move.uid, at: move.at, ...(move.parentUid !== undefined ? { parentUid: move.parentUid } : {}) }])
    },
    [tasks, collapsed, sendCommands],
  )

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
        // Navigate over the visible list so folded subtrees are jumped over. An anchor
        // hidden by a collapse falls back to the top/bottom of the visible list.
        const currentIndex = anchorUid !== null ? (visibleIndexByUid.get(anchorUid) ?? -1) : -1
        const nextIndex = event.key === 'ArrowUp'
          ? (currentIndex <= 0 ? 0 : currentIndex - 1)
          : (currentIndex < 0 ? visible.length - 1 : Math.min(visible.length - 1, currentIndex + 1))
        const next = visible[nextIndex]
        if (next !== undefined) selectTask(next.uid, { toggle: false, range: event.shiftKey })
        return
      }

      if (editable && event.altKey && event.shiftKey && (event.key === 'ArrowRight' || event.key === 'ArrowLeft')) {
        event.preventDefault()
        forSelection(event.key === 'ArrowRight' ? 'indentTask' : 'outdentTask')
        return
      }

      // Plain Left/Right fold or unfold the selected summary (view-only, no Alt/Shift).
      if (!event.altKey && !event.shiftKey && !meta && (event.key === 'ArrowLeft' || event.key === 'ArrowRight')) {
        if (selected !== null && isCollapsible(tasks, selected.uid)) {
          event.preventDefault()
          toggleCollapse(selected.uid, event.key === 'ArrowLeft' ? 'collapse' : 'expand')
        }
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

  function download(kind: 'p27' | 'mspdi' | 'csv') {
    const path =
      kind === 'p27'
        ? `/api/projects/${projectId}/file`
        : kind === 'mspdi'
          ? `/api/projects/${projectId}/export/mspdi`
          : `/api/projects/${projectId}/export/csv`
    const fallbackName = kind === 'p27' ? 'project.p27' : kind === 'mspdi' ? 'project.xml' : 'project.csv'
    void client.download(path, fallbackName).catch((cause: unknown) => setError(cause instanceof Error ? cause.message : String(cause)))
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

  // Inspector docking (Gantt view only — other views keep the floating overlay since
  // they have no shared split layout to dock into): it's a normal grid column that
  // pushes the Gantt pane's width, sized to match the panel or its collapsed tab.
  const showTaskInspector = inspectorScope === 'task' && selected !== null && schedule !== null && !inspectorCollapsed
  const showMultiInspector = inspectorScope === 'task' && selectedTasks.length > 1 && schedule !== null && !inspectorCollapsed
  const showProjectInspector = inspectorScope === 'project' && schedule !== null && !inspectorCollapsed
  const showInspectorTab = inspectorCollapsed && (inspectorScope === 'project' || selectedTasks.length > 0)
  const dockedInspectorWidth =
    viewMode === 'gantt'
      ? showTaskInspector || showMultiInspector || showProjectInspector
        ? ' 330px'
        : showInspectorTab
          ? ' 26px'
          : ''
      : ''

  /** Rare/global actions that don't belong on the hot path: grouped under the ⋯ menu.
   *  Available to readers too (D6, readers never blocked) — Reports/Export/Manage all view state. */
  const overflowGroups: MenuGroup[] = [
    {
      heading: 'MANAGE',
      items: [
        { label: 'Custom fields', onClick: () => setDialog('fields') },
        { label: 'Calendars', onClick: () => setDialog('calendars') },
      ],
    },
    ...(viewMode === 'gantt' || viewMode === 'resources' || viewMode === 'table'
      ? [{ heading: 'VIEW', items: [{ label: 'Columns…', onClick: () => setDialog('columns') }] }]
      : []),
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
        { label: 'Task list (CSV)', onClick: () => download('csv') },
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
              <Icon name="Menu" size={12} />
            </button>
          )}
          groups={[
            { items: [{ label: '← All projects', onClick: onBack }] },
            {
              items: [
                { label: dark ? '☀ Light mode' : '☾ Dark mode', onClick: onToggleTheme },
              ],
            },
            {
              items: [
                {
                  label: 'Help & docs',
                  onClick: () =>
                    window.open(
                      `https://github.com/a-27m/project27/blob/${imageTag !== null ? 'v' + imageTag : 'main'}/docs/guide.md`,
                      '_blank',
                      'noopener',
                    ),
                },
              ],
            },
          ]}
        />
        <button
          className="project-btn"
          onClick={() => {
            if (inspectorScope === 'project' && !inspectorCollapsed) {
              setInspectorCollapsed(true)
            } else {
              setInspectorScope('project')
              setInspectorCollapsed(false)
            }
          }}
          title="Project info & settings"
        >
          {schedule?.project.name ?? '…'}
        </button>
        {schedule !== null && (
          <button className="version-chip" onClick={() => setDialog('history')} title="Version history">
            v{schedule.version}
          </button>
        )}
        {schedule !== null && <StatusDateBadge statusDate={schedule.project.statusDate} />}
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
          <span className="dropdown checkin-split" ref={checkinAnchorRef}>
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
            {checkinOpen && (
              <CheckinPopover
                version={schedule?.version ?? 0}
                anchorRef={checkinAnchorRef}
                onCheckin={(comment) => void checkin(comment)}
                onClose={() => setCheckinOpen(false)}
              />
            )}
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
          groups={[{ items: [{ label: 'Sign out', onClick: onSignOut }] }]}
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
              <DropdownMenu
                ariaLabel="Insert"
                trigger={({ open, toggle }) => (
                  <span className="split-btn">
                    <button className="icon-btn split-btn-main" onClick={() => addTask('task')} title="Add task (N)">
                      <Icon name="Add" size={12} />
                      Task
                    </button>
                    <button
                      className="icon-btn split-btn-caret"
                      onClick={toggle}
                      title="More insert options"
                      aria-haspopup="menu"
                      aria-expanded={open}
                    >
                      <Icon name="CaretDown" size={12} />
                    </button>
                  </span>
                )}
                groups={[
                  {
                    heading: 'INSERT',
                    items: [
                      { label: '+ Milestone', onClick: () => addTask('milestone') },
                      { label: '+ Recurring task…', onClick: () => setDialog('recurring') },
                    ],
                  },
                ]}
              />
            </span>
            {selectionRoots.length > 0 && (
              <span className="action-group" role="group" aria-label="Structure">
                <button className="icon-btn" onClick={() => setSpaceAfter(1)} title="Add space below the selected row(s)">
                  Space +
                </button>
                <button className="icon-btn" onClick={() => setSpaceAfter(-1)} title="Remove space below the selected row(s)">
                  Space −
                </button>
                <button className="icon-btn" onClick={() => forSelection('outdentTask')} title="Outdent (Alt+Shift+←)" aria-label="Outdent">
                  <Icon name="ArrowRight" size={12} style={{ transform: 'scaleX(-1)' }} />
                </button>
                <button className="icon-btn" onClick={() => forSelection('indentTask')} title="Indent (Alt+Shift+→)" aria-label="Indent">
                  <Icon name="ArrowRight" size={12} />
                </button>
                <button className="icon-btn" disabled={selected === null} onClick={() => moveSelection('up')} title="Move up (Ctrl+↑)" aria-label="Move up">
                  <Icon name="ArrowUp" size={12} />
                </button>
                <button className="icon-btn" disabled={selected === null} onClick={() => moveSelection('down')} title="Move down (Ctrl+↓)" aria-label="Move down">
                  <Icon name="ArrowDown" size={12} />
                </button>
                <button className="icon-btn danger" onClick={deleteSelection} title="Delete (Del)">
                  <Icon name="Close" size={12} />
                  Delete
                </button>
              </span>
            )}
            {selected !== null && !selected.summary && !selected.milestone && (
              <span className="action-group" role="group" aria-label="Progress & assignment">
                <SegmentedPercent value={selected.percentComplete} editable={editable} onCommit={setPercent} />
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
          <DropdownMenu
            ariaLabel="Baseline"
            trigger={({ open, toggle }) => <DropdownTrigger label="Baseline" open={open} onClick={toggle} />}
            groups={[
              ...(editable
                ? [
                    {
                      items: [
                        { label: 'Set baseline', onClick: () => void sendCommands([{ op: 'setBaseline' }]) },
                        { label: 'Clear baseline', onClick: () => void sendCommands([{ op: 'clearBaseline' }]) },
                      ],
                    },
                  ]
                : []),
              {
                items: [
                  {
                    label: 'Show baseline (ghost bars)',
                    checked: showBaselineGhosts,
                    onClick: () => setShowBaselineGhosts((v) => !v),
                  },
                ],
              },
            ]}
          />
          {editable && (
            <>
              <DropdownMenu
                ariaLabel="Level"
                trigger={({ open, toggle }) => <DropdownTrigger label="Level" open={open} onClick={toggle} />}
                groups={[
                  {
                    items: [
                      { label: 'Level resources', onClick: () => void sendCommands([{ op: 'level' }]) },
                      { label: 'Level with options…', onClick: () => setDialog('level') },
                      { label: 'Clear leveling', onClick: () => void sendCommands([{ op: 'clearLeveling' }]) },
                    ],
                  },
                ]}
              />
              <button
                className="icon-btn"
                onClick={() => void sendCommands([{ op: 'reschedule' }])}
                title="Push uncompleted work past the status date"
              >
                Reschedule Incomplete Work
              </button>
            </>
          )}
          {/* Rare/global actions, incl. read-only ones (Reports, Download): available to readers too — D6, readers never blocked. */}
          <DropdownMenu
            ariaLabel="More project actions"
            align="right"
            trigger={({ open, toggle }) => (
              <button className="icon-btn" onClick={toggle} title="More project actions" aria-haspopup="menu" aria-expanded={open}>
                <Icon name="OverflowMenuHorizontal" size={12} />
              </button>
            )}
            groups={overflowGroups}
          />
        </span>
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
          <TableView
            client={client}
            projectId={projectId}
            version={schedule?.version ?? 0}
            table={tableSubview}
            onTableChange={setTableSubview}
            columnKeys={storedTableColumnKeys}
          />
        </div>
      )}
      {viewMode === 'resources' && schedule !== null && (
        <div className="view-body">
          <ResourcesView
            project={schedule.project}
            editable={editable}
            onCommands={(commands) => void sendCommands(commands)}
            columnKeys={resourcesColumnKeys}
          />
        </div>
      )}
      <div
        className="split"
        style={{
          gridTemplateColumns: `${splitX}px 6px 1fr${dockedInspectorWidth}`,
          display: viewMode === 'gantt' ? undefined : 'none',
        }}
      >
        <div
          className="pane"
          ref={scrollerRef}
          onScroll={(event) => syncScroll(event.currentTarget, ganttScrollRef.current, setScrollTop)}
        >
          <div className="sheet-header" style={{ width: gridWidth, height: HEADER_HEIGHT }}>
            {columns.map((column) => (
              <span
                key={column.key}
                className={'cell header' + (column.numeric === true ? ' num' : '')}
                style={{ width: column.width }}
              >
                {column.label}
              </span>
            ))}
          </div>
          <TaskSheet
            displayRows={displayRows}
            allTasks={tasks}
            columns={columns}
            gridWidth={gridWidth}
            context={columnContext}
            rowHeight={ROW_HEIGHT}
            window_={window_}
            editable={editable}
            selectedUids={selectedUids}
            collapsedUids={collapsed}
            onSelect={(uid, modifiers) => selectTask(uid, modifiers)}
            onToggleCollapse={toggleCollapse}
            onReorder={dropRow}
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
            showBaselineGhosts={showBaselineGhosts}
            onSelect={(uid) => selectTask(uid)}
            onCommands={(commands) => void sendCommands(commands)}
          />
          {viewMode === 'gantt' && (
            <div className="gantt-floating-controls">
              <button className="icon-btn" onClick={() => setZoomIndex((z) => Math.max(0, z - 1))} disabled={zoomIndex === 0} title="Zoom out" aria-label="Zoom out">
                <Icon name="Subtract" size={12} />
              </button>
              <span className="zoom-label">{pxPerDay}px/d</span>
              <button
                className="icon-btn"
                onClick={() => setZoomIndex((z) => Math.min(ZOOM_LEVELS.length - 1, z + 1))}
                disabled={zoomIndex === ZOOM_LEVELS.length - 1}
                title="Zoom in"
                aria-label="Zoom in"
              >
                <Icon name="Add" size={12} />
              </button>
            </div>
          )}
        </div>
        {viewMode === 'gantt' && showTaskInspector && selected !== null && schedule !== null && (
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
        {viewMode === 'gantt' && showMultiInspector && schedule !== null && (
          <MultiTaskInspector
            tasks={selectedTasks}
            project={schedule.project}
            editable={editable}
            onSetPercent={setPercent}
            onAssign={assignToSelection}
            onIndent={() => forSelection('indentTask')}
            onOutdent={() => forSelection('outdentTask')}
            onDelete={deleteSelection}
            onClose={() => selectTask(null)}
            onCollapse={() => setInspectorCollapsed(true)}
          />
        )}
        {viewMode === 'gantt' && inspectorScope === 'project' && schedule !== null && !inspectorCollapsed && (
          <ProjectInspector
            project={schedule.project}
            editable={editable}
            onCommands={(commands) => void sendCommands(commands)}
            onClose={() => setInspectorCollapsed(true)}
            onCollapse={() => setInspectorCollapsed(true)}
            onOpenCalendars={() => setDialog('calendars')}
            onOpenCustomFields={() => setDialog('fields')}
          />
        )}
        {viewMode === 'gantt' && inspectorCollapsed && (inspectorScope === 'project' || selectedTasks.length > 0) && (
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
      {dialog === 'columns' && columnsDialogConfig !== null && (
        <ColumnsDialog
          title={columnsDialogConfig.title}
          options={columnsDialogConfig.options}
          selectedKeys={columnsDialogConfig.selectedKeys}
          mandatoryKey={columnsDialogConfig.mandatoryKey}
          defaultKeys={columnsDialogConfig.defaultKeys}
          onChange={columnsDialogConfig.onChange}
          onClose={() => setDialog(null)}
        />
      )}
      {dialog === 'level' && (
        <LevelDialog onCommand={(command) => void sendCommands([command])} onClose={() => setDialog(null)} />
      )}
      {viewMode !== 'gantt' && showTaskInspector && selected !== null && schedule !== null && (
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
      {viewMode !== 'gantt' && showMultiInspector && schedule !== null && (
        <MultiTaskInspector
          tasks={selectedTasks}
          project={schedule.project}
          editable={editable}
          onSetPercent={setPercent}
          onAssign={assignToSelection}
          onIndent={() => forSelection('indentTask')}
          onOutdent={() => forSelection('outdentTask')}
          onDelete={deleteSelection}
          onClose={() => selectTask(null)}
          onCollapse={() => setInspectorCollapsed(true)}
        />
      )}
      {viewMode !== 'gantt' && inspectorScope === 'project' && schedule !== null && !inspectorCollapsed && (
        <ProjectInspector
          project={schedule.project}
          editable={editable}
          onCommands={(commands) => void sendCommands(commands)}
          onClose={() => setInspectorCollapsed(true)}
          onCollapse={() => setInspectorCollapsed(true)}
          onOpenCalendars={() => setDialog('calendars')}
          onOpenCustomFields={() => setDialog('fields')}
        />
      )}
      {viewMode !== 'gantt' && inspectorCollapsed && (inspectorScope === 'project' || selectedTasks.length > 0) && (
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

/** Check-in-with-comment popover, opened from the check-in split button's caret.
 *  Portaled to document.body with a viewport-fixed position instead of being an
 *  absolutely-positioned descendant of the toolbar: its content is tall enough to
 *  overlap the Gantt pane below, and nesting it under the toolbar put it in the same
 *  stacking context as the pane's sticky date header — a header/popover ordering
 *  fight that a body-level portal sidesteps entirely, regardless of z-index tuning. */
function CheckinPopover({
  version,
  anchorRef,
  onCheckin,
  onClose,
}: {
  version: number
  anchorRef: React.RefObject<HTMLElement | null>
  onCheckin: (comment: string) => void
  onClose: () => void
}) {
  const [comment, setComment] = useState('')
  const ref = useRef<HTMLDivElement>(null)
  useOutsideClose(ref, true, onClose)
  const [position, setPosition] = useState<{ top: number; right: number } | null>(null)
  useEffect(() => {
    const rect = anchorRef.current?.getBoundingClientRect()
    if (rect !== undefined) setPosition({ top: rect.bottom + 4, right: window.innerWidth - rect.right })
  }, [anchorRef])
  if (position === null) return null
  return createPortal(
    <div
      className="dropdown-panel align-right checkin-popover checkin-popover-portal"
      ref={ref}
      role="dialog"
      aria-label="Check in with a comment"
      style={{ top: position.top, right: position.right }}
    >
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
    </div>,
    document.body,
  )
}

const LEVELING_ORDERS = ['priorityStandard', 'standard', 'idOnly'] as const
const LEVELING_ORDER_LABELS = ['Priority, standard', 'Standard', 'ID only']
const LEVELING_GRANULARITIES = ['day', 'minute'] as const
const LEVELING_GRANULARITY_LABELS = ['Day', 'Minute']

function LevelDialog({ onCommand, onClose }: { onCommand: (command: Command) => void; onClose: () => void }) {
  const dialogRef = useRef<HTMLDivElement>(null)
  const [order, setOrder] = useState<(typeof LEVELING_ORDERS)[number]>('priorityStandard')
  const [granularity, setGranularity] = useState<(typeof LEVELING_GRANULARITIES)[number]>('day')
  const [splitInProgress, setSplitInProgress] = useState(false)
  useEffect(() => {
    dialogRef.current?.focus()
  }, [])
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
        aria-label="Level resources…"
        tabIndex={-1}
        ref={dialogRef}
        onClick={(event) => event.stopPropagation()}
      >
        <h3>Level resources…</h3>
        <label className="inspector-row">
          <span className="inspector-label">Order</span>
          <select value={order} onChange={(event) => setOrder(event.target.value as (typeof LEVELING_ORDERS)[number])}>
            {LEVELING_ORDERS.map((value, index) => (
              <option key={value} value={value}>
                {LEVELING_ORDER_LABELS[index]}
              </option>
            ))}
          </select>
        </label>
        <label className="inspector-row">
          <span className="inspector-label">Granularity</span>
          <select
            value={granularity}
            onChange={(event) => setGranularity(event.target.value as (typeof LEVELING_GRANULARITIES)[number])}
          >
            {LEVELING_GRANULARITIES.map((value, index) => (
              <option key={value} value={value}>
                {LEVELING_GRANULARITY_LABELS[index]}
              </option>
            ))}
          </select>
        </label>
        <label className="inspector-row">
          <span className="inspector-label">Split in-progress tasks</span>
          <input type="checkbox" checked={splitInProgress} onChange={(event) => setSplitInProgress(event.target.checked)} />
        </label>
        <div className="modal-actions">
          <button onClick={onClose}>Cancel</button>
          <button
            className="primary"
            onClick={() => {
              onCommand({ op: 'level', order, granularity, splitInProgress })
              onClose()
            }}
          >
            Level
          </button>
        </div>
      </div>
    </div>
  )
}

/** Amber/highlighted when statusDate differs from today; a plain muted chip when equal or unset. */
function StatusDateBadge({ statusDate }: { statusDate: string | null }) {
  if (statusDate === null) {
    return (
      <span className="status-chip" title="Status date — the as-of date for tracking & earned value">
        STATUS —
      </span>
    )
  }
  const status = fromWireDate(statusDate)
  const statusDay = new Date(status.getFullYear(), status.getMonth(), status.getDate())
  const now = new Date()
  const today = new Date(now.getFullYear(), now.getMonth(), now.getDate())
  const gapDays = Math.round((today.getTime() - statusDay.getTime()) / 86_400_000)
  const label = status.toLocaleDateString('en-US', { month: 'short', day: 'numeric' })
  if (gapDays === 0) {
    return (
      <span className="status-chip" title="Status date — the as-of date for tracking & earned value">
        STATUS {label}
      </span>
    )
  }
  const gapText = gapDays > 0 ? `${gapDays}d behind today` : `${-gapDays}d ahead of today`
  return (
    <span className="status-chip behind" title="Status date — the as-of date for tracking & earned value">
      ⚑ Status {label} <span className="status-gap">· {gapText}</span>
    </span>
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
