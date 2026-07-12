import { useState } from 'react'
import type { Command, ScheduleTask } from '../api/types'
import { durationDays } from '../lib/format'
import type { RowWindow } from '../lib/virtualize'
import type { ColumnContext, SheetColumn } from './sheetColumns'

interface Props {
  tasks: ScheduleTask[]
  columns: readonly SheetColumn[]
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
  tasks,
  columns,
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
    const task = tasks.find((candidate) => candidate.uid === edit.uid)
    setEdit(null)
    if (task === undefined) return
    const value = edit.value.trim()
    if (edit.field === 'name' && value !== task.name) {
      onCommands([{ op: 'setTask', uid: task.uid, name: value }])
    } else if (edit.field === 'duration' && value !== '' && value !== durationDays(task.durationMinutes, context.minutesPerDay)) {
      onCommands([{ op: 'setTask', uid: task.uid, duration: value }])
    }
  }

  function beginEdit(task: ScheduleTask, field: CellEdit['field']) {
    if (!editable) return
    if (field === 'duration' && (task.summary || task.name === '')) return
    setEdit({
      uid: task.uid,
      field,
      value: field === 'name' ? task.name : durationDays(task.durationMinutes, context.minutesPerDay, task.estimated),
    })
  }

  const visible = tasks.slice(window_.first, window_.last)

  return (
    <div className="sheet-body" style={{ height: window_.totalHeight }} role="grid" aria-label="Tasks">
      <div style={{ transform: `translateY(${window_.offsetY}px)` }}>
        {visible.map((task) => {
          const blank = task.name === ''
          return (
            <div
              key={task.uid}
              role="row"
              aria-selected={selectedUids.has(task.uid)}
              className={
                'sheet-row' +
                (selectedUids.has(task.uid) ? ' selected' : '') +
                (task.summary ? ' summary' : '') +
                (task.active ? '' : ' inactive') +
                (blank ? ' blank' : '')
              }
              style={{ height: rowHeight }}
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
                    className={'cell' + (isName ? ' name' : '')}
                    style={{
                      width: column.width,
                      ...(isName ? { paddingLeft: 8 + task.outlineLevel * 16 } : {}),
                    }}
                    onDoubleClick={editableCell ? () => beginEdit(task, isName ? 'name' : 'duration') : undefined}
                    title={isName && !blank ? `${task.wbs} ${task.name} (uid:${task.uid})` : undefined}
                  >
                    {editing && edit !== null ? (
                      <EditInput edit={edit} onChange={setEdit} onCommit={commitEdit} onCancel={() => setEdit(null)} />
                    ) : blank && !isName && column.key !== 'row' && column.key !== 'uid' ? (
                      ''
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
