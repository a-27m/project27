import { useState } from 'react'
import type { Command, ScheduleTask } from '../api/types'
import type { DisplayRow } from '../lib/displayRows'
import { durationDays } from '../lib/format'
import type { RowWindow } from '../lib/virtualize'
import type { ColumnContext, SheetColumn } from './sheetColumns'

interface Props {
  displayRows: DisplayRow<ScheduleTask>[]
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
  onSelect: (uid: number, modifiers: { toggle: boolean; range: boolean }) => void
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
  columns,
  gridWidth,
  context,
  rowHeight,
  window_,
  editable,
  selectedUids,
  onSelect,
  onCommands,
}: Props) {
  const [edit, setEdit] = useState<CellEdit | null>(null)

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

  const visible = displayRows.slice(window_.first, window_.last)

  return (
    <div className="sheet-body" style={{ height: window_.totalHeight, width: gridWidth }} role="grid" aria-label="Tasks">
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
          return (
            <div
              key={task.uid}
              role="row"
              aria-selected={selectedUids.has(task.uid)}
              className={
                'sheet-row' +
                (selectedUids.has(task.uid) ? ' selected' : '') +
                (task.summary ? ' summary' : '') +
                (task.active ? '' : ' inactive')
              }
              style={{ height: rowHeight, width: gridWidth }}
              onClick={(event) =>
                onSelect(task.uid, { toggle: event.metaKey || event.ctrlKey, range: event.shiftKey })
              }
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
                      ...(isName ? { paddingLeft: 8 + task.outlineLevel * 16 } : {}),
                    }}
                    onDoubleClick={editableCell ? () => beginEdit(task, isName ? 'name' : 'duration') : undefined}
                    title={isName ? `${task.wbs} ${task.name} (uid:${task.uid})` : undefined}
                  >
                    {editing && edit !== null ? (
                      <EditInput edit={edit} onChange={setEdit} onCommit={commitEdit} onCancel={() => setEdit(null)} />
                    ) : (
                      column.render(task, context)
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
