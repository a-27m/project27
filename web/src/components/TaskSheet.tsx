import { useMemo, useRef, useState } from 'react'
import type { Command, ScheduleTask } from '../api/types'
import type { DisplayRow } from '../lib/displayRows'
import { durationDays } from '../lib/format'
import { beginRowDrag, dragMoved, dropTarget, moveRowDrag, type RowDrag } from '../lib/rowDrag'
import type { RowWindow } from '../lib/virtualize'
import { Icon } from './icons/Icon'
import type { ColumnContext, SheetColumn } from './sheetColumns'

const NAME_BASE_PX = 8
const NAME_INDENT_PX = 16

interface Props {
  displayRows: DisplayRow<ScheduleTask>[]
  /** Full model task list (all levels, folded rows included) — for drop resolution. */
  allTasks: readonly ScheduleTask[]
  columns: readonly SheetColumn[]
  /** Total width of all columns — rows are given this explicit width so their
   *  border/background still cover the rightmost columns once horizontal scroll
   *  kicks in (a row with no explicit width only spans the visible viewport). */
  gridWidth: number
  context: ColumnContext
  rowHeight: number
  window_: RowWindow
  editable: boolean
  selectedUids: ReadonlySet<number>
  collapsedUids: ReadonlySet<number>
  onSelect: (uid: number, modifiers: { toggle: boolean; range: boolean }) => void
  onToggleCollapse: (uid: number) => void
  /** Move a dragged task: aboveUid = visible task above the drop line (null = top). */
  onReorder: (draggedUid: number, aboveUid: number | null, indentHint: number) => void
  onCommands: (commands: Command[]) => void
}

interface CellEdit {
  uid: number
  field: 'name' | 'duration'
  value: string
}

/** The sheet body rows; the header lives in the parent so both panes share one scroller. */
export function TaskSheet({
  displayRows,
  allTasks,
  columns,
  gridWidth,
  context,
  rowHeight,
  window_,
  editable,
  selectedUids,
  collapsedUids,
  onSelect,
  onToggleCollapse,
  onReorder,
  onCommands,
}: Props) {
  const [edit, setEdit] = useState<CellEdit | null>(null)
  const [draggingUid, setDraggingUid] = useState<number | null>(null)
  const [indicator, setIndicator] = useState<{ boundary: number; level: number } | null>(null)
  const bodyRef = useRef<HTMLDivElement>(null)
  const dragRef = useRef<RowDrag | null>(null)
  const movedRef = useRef(false)
  const dropRef = useRef<{ aboveUid: number | null; indentHint: number } | null>(null)

  // Content-space x where the name column's text (outline level 0) begins.
  const nameLeftOffset = useMemo(() => {
    let x = 0
    for (const column of columns) {
      if (column.key === 'name') break
      x += column.width
    }
    return x + NAME_BASE_PX
  }, [columns])

  function commitEdit() {
    if (edit === null) return
    const row = displayRows.find((candidate) => candidate.kind === 'task' && candidate.task.uid === edit.uid)
    const task = row?.kind === 'task' ? row.task : undefined
    setEdit(null)
    if (task === undefined) return
    const value = edit.value.trim()
    if (edit.field === 'name' && value !== '' && value !== task.name) {
      onCommands([{ op: 'setTask', uid: task.uid, name: value }])
    } else if (edit.field === 'duration' && value !== '' && value !== durationDays(task.durationMinutes, context.minutesPerDay)) {
      onCommands([{ op: 'setTask', uid: task.uid, duration: value }])
    }
  }

  function beginEdit(task: ScheduleTask, field: CellEdit['field']) {
    if (!editable) return
    if (field === 'duration' && task.summary) return
    setEdit({
      uid: task.uid,
      field,
      value: field === 'name' ? task.name : durationDays(task.durationMinutes, context.minutesPerDay, task.estimated),
    })
  }

  // The drop line (0..displayRows.length) and the visible task just above it.
  function boundaryFrom(clientY: number): number {
    const rect = bodyRef.current?.getBoundingClientRect()
    if (rect === undefined) return 0
    const raw = Math.round((clientY - rect.top) / rowHeight)
    return Math.max(0, Math.min(displayRows.length, raw))
  }

  function taskAbove(boundary: number): number | null {
    for (let i = boundary - 1; i >= 0; i--) {
      const row = displayRows[i]
      if (row.kind === 'task') return row.task.uid
    }
    return null
  }

  function onRowPointerDown(task: ScheduleTask, event: React.PointerEvent) {
    if (!editable || event.button !== 0) return
    // Record the potential drag but do NOT capture yet: capturing here would steal the
    // click/double-click/shift-click that plain selection and inline editing rely on.
    dragRef.current = beginRowDrag(task.uid, event.clientX, event.clientY)
    movedRef.current = false
    dropRef.current = null
  }

  function onBodyPointerMove(event: React.PointerEvent) {
    const drag = dragRef.current
    if (drag === null) return
    const next = moveRowDrag(drag, event.clientX, event.clientY)
    dragRef.current = next
    if (!dragMoved(next)) return
    if (!movedRef.current) {
      setDraggingUid(next.uid)
      // Now that it's a real drag, capture on the stable body so we keep receiving
      // moves outside the sheet and as rows recycle during a scroll mid-drag.
      bodyRef.current?.setPointerCapture(event.pointerId)
    }
    movedRef.current = true
    const boundary = boundaryFrom(event.clientY)
    const aboveUid = taskAbove(boundary)
    // Reorder only: keep the task's own outline level where the drop point allows it
    // (dropTarget clamps to the legal range). No horizontal control of indent level.
    const indentHint = allTasks.find((task) => task.uid === next.uid)?.outlineLevel ?? 0
    const target = dropTarget(allTasks, next.uid, aboveUid, indentHint, collapsedUids)
    if (target === null) {
      dropRef.current = null
      setIndicator(null)
    } else {
      dropRef.current = { aboveUid, indentHint }
      setIndicator({ boundary, level: target.level })
    }
  }

  function onBodyPointerUp() {
    if (dragRef.current === null) return
    const drag = dragRef.current
    dragRef.current = null
    setDraggingUid(null)
    setIndicator(null)
    if (movedRef.current && dropRef.current !== null) {
      onReorder(drag.uid, dropRef.current.aboveUid, dropRef.current.indentHint)
    }
    dropRef.current = null
  }

  const visible = displayRows.slice(window_.first, window_.last)

  return (
    <div
      ref={bodyRef}
      className="sheet-body"
      style={{ height: window_.totalHeight, width: gridWidth, position: 'relative' }}
      role="grid"
      aria-label="Tasks"
      onPointerMove={onBodyPointerMove}
      onPointerUp={onBodyPointerUp}
    >
      {indicator !== null && (
        <div
          className="sheet-drop-indicator"
          aria-hidden="true"
          style={{
            top: indicator.boundary * rowHeight - 1,
            left: nameLeftOffset + indicator.level * NAME_INDENT_PX,
            width: Math.max(24, gridWidth - nameLeftOffset - indicator.level * NAME_INDENT_PX - 8),
          }}
        />
      )}
      <div style={{ transform: `translateY(${window_.offsetY}px)`, width: gridWidth }}>
        {visible.map((row) => {
          if (row.kind === 'gap') {
            return (
              <div
                key={`gap-${row.afterUid}-${row.index}`}
                role="presentation"
                aria-hidden="true"
                className="sheet-row sheet-gap"
                style={{ height: rowHeight, width: gridWidth }}
              />
            )
          }

          const task = row.task
          const collapsed = collapsedUids.has(task.uid)
          return (
            <div
              key={task.uid}
              role="row"
              aria-selected={selectedUids.has(task.uid)}
              {...(task.summary ? { 'aria-expanded': !collapsed } : {})}
              className={
                'sheet-row' +
                (selectedUids.has(task.uid) ? ' selected' : '') +
                (task.summary ? ' summary' : '') +
                (task.active ? '' : ' inactive') +
                (draggingUid === task.uid ? ' dragging' : '')
              }
              style={{ height: rowHeight, width: gridWidth }}
              onPointerDown={(event) => onRowPointerDown(task, event)}
              onClick={(event) => {
                if (movedRef.current) {
                  movedRef.current = false
                  return
                }
                onSelect(task.uid, { toggle: event.metaKey || event.ctrlKey, range: event.shiftKey })
              }}
            >
              {columns.map((column) => {
                const isName = column.key === 'name'
                const editing =
                  edit !== null && edit.uid === task.uid && (edit.field === column.key || (edit.field === 'name' && isName))
                const editableCell = isName || column.key === 'duration'
                return (
                  <span
                    key={column.key}
                    role="gridcell"
                    className={
                      'cell' +
                      (isName ? ' name' : '') +
                      (column.mono === true ? ' mono' : '') +
                      (column.numeric === true ? ' num' : '')
                    }
                    style={{
                      width: column.width,
                      ...(isName ? { paddingLeft: NAME_BASE_PX + task.outlineLevel * NAME_INDENT_PX } : {}),
                    }}
                    onDoubleClick={editableCell ? () => beginEdit(task, isName ? 'name' : 'duration') : undefined}
                    title={
                      isName
                        ? `${task.wbs} ${task.name} (uid:${task.uid})` + (task.hasDescription ? ' — has a description' : '')
                        : undefined
                    }
                  >
                    {editing && edit !== null ? (
                      <EditInput edit={edit} onChange={setEdit} onCommit={commitEdit} onCancel={() => setEdit(null)} />
                    ) : (
                      <>
                        {isName &&
                          (task.summary ? (
                            <button
                              type="button"
                              className="sheet-twisty"
                              aria-label={collapsed ? 'Expand' : 'Collapse'}
                              onPointerDown={(event) => event.stopPropagation()}
                              onClick={(event) => {
                                event.stopPropagation()
                                onToggleCollapse(task.uid)
                              }}
                            >
                              <Icon name={collapsed ? 'CaretRight' : 'CaretDown'} size={12} />
                            </button>
                          ) : (
                            <span className="sheet-twisty-spacer" aria-hidden="true" />
                          ))}
                        {column.render(task, context)}
                        {isName && task.hasDescription && (
                          <Icon name="Document" size={12} className="name-description-flag" />
                        )}
                      </>
                    )}
                  </span>
                )
              })}
            </div>
          )
        })}
      </div>
    </div>
  )
}

function EditInput({
  edit,
  onChange,
  onCommit,
  onCancel,
}: {
  edit: CellEdit
  onChange: (edit: CellEdit) => void
  onCommit: () => void
  onCancel: () => void
}) {
  return (
    <input
      className="cell-edit"
      value={edit.value}
      autoFocus
      onFocus={(event) => event.target.select()}
      onChange={(event) => onChange({ ...edit, value: event.target.value })}
      onBlur={onCommit}
      onKeyDown={(event) => {
        if (event.key === 'Enter') onCommit()
        else if (event.key === 'Escape') onCancel()
        event.stopPropagation()
      }}
      onClick={(event) => event.stopPropagation()}
    />
  )
}
