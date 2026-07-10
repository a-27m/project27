import { useState } from 'react'
import type { Command, ScheduleTask } from '../api/types'
import { dateTime, durationDays, predecessorToken } from '../lib/format'
import type { RowWindow } from '../lib/virtualize'
import { SHEET_COLUMNS } from './sheetColumns'

interface Props {
  tasks: ScheduleTask[]
  rowByUid: ReadonlyMap<number, number>
  minutesPerDay: number
  rowHeight: number
  window_: RowWindow
  editable: boolean
  selectedUid: number | null
  onSelect: (uid: number | null) => void
  onCommands: (commands: Command[]) => void
}

interface CellEdit {
  uid: number
  field: 'name' | 'duration'
  value: string
}

/** The sheet body rows (headers live in the parent so both panes share one scroller). */
export function TaskSheet({
  tasks,
  rowByUid,
  minutesPerDay,
  rowHeight,
  window_,
  editable,
  selectedUid,
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
    if (value === '') return
    if (edit.field === 'name' && value !== task.name) {
      onCommands([{ op: 'setTask', uid: task.uid, name: value }])
    } else if (edit.field === 'duration' && value !== durationDays(task.durationMinutes, minutesPerDay)) {
      onCommands([{ op: 'setTask', uid: task.uid, duration: value }])
    }
  }

  function beginEdit(task: ScheduleTask, field: CellEdit['field']) {
    if (!editable) return
    if (field === 'duration' && task.summary) return
    setEdit({
      uid: task.uid,
      field,
      value: field === 'name' ? task.name : durationDays(task.durationMinutes, minutesPerDay, task.estimated),
    })
  }

  const visible = tasks.slice(window_.first, window_.last)

  return (
    <div className="sheet-body" style={{ height: window_.totalHeight }}>
      <div style={{ transform: `translateY(${window_.offsetY}px)` }}>
        {visible.map((task) => (
          <div
            key={task.uid}
            className={
              'sheet-row' +
              (task.uid === selectedUid ? ' selected' : '') +
              (task.summary ? ' summary' : '') +
              (task.active ? '' : ' inactive')
            }
            style={{ height: rowHeight }}
            onClick={() => onSelect(task.uid === selectedUid ? null : task.uid)}
          >
            <span className="cell" style={{ width: SHEET_COLUMNS[0].width }}>
              {task.row}
            </span>
            <span
              className="cell name"
              style={{ width: SHEET_COLUMNS[1].width, paddingLeft: 8 + task.outlineLevel * 16 }}
              onDoubleClick={() => beginEdit(task, 'name')}
              title={task.wbs + ' ' + task.name}
            >
              {edit !== null && edit.uid === task.uid && edit.field === 'name' ? (
                <EditInput edit={edit} onChange={setEdit} onCommit={commitEdit} onCancel={() => setEdit(null)} />
              ) : (
                task.name
              )}
            </span>
            <span className="cell" style={{ width: SHEET_COLUMNS[2].width }} onDoubleClick={() => beginEdit(task, 'duration')}>
              {edit !== null && edit.uid === task.uid && edit.field === 'duration' ? (
                <EditInput edit={edit} onChange={setEdit} onCommit={commitEdit} onCancel={() => setEdit(null)} />
              ) : (
                durationDays(task.durationMinutes, minutesPerDay, task.estimated)
              )}
            </span>
            <span className="cell" style={{ width: SHEET_COLUMNS[3].width }}>
              {dateTime(task.start)}
            </span>
            <span className="cell" style={{ width: SHEET_COLUMNS[4].width }}>
              {dateTime(task.finish)}
            </span>
            <span className="cell" style={{ width: SHEET_COLUMNS[5].width }}>
              {task.predecessors
                .map((link) =>
                  predecessorToken(
                    rowByUid.get(link.predecessorUid) ?? 0,
                    link.type,
                    link.lagKind,
                    link.lagValue,
                    minutesPerDay,
                  ),
                )
                .join(',')}
            </span>
          </div>
        ))}
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
      }}
      onClick={(event) => event.stopPropagation()}
    />
  )
}
