import { useState } from 'react'
import type { Command, ScheduleProject } from '../api/types'

interface Props {
  project: ScheduleProject
  editable: boolean
  onCommands: (commands: Command[]) => void
  onClose: () => void
}

/** Project settings dialog: identity, anchors, calendar, time settings, status date (12p-3). */
export function ProjectSettings({ project, editable, onCommands, onClose }: Props) {
  const [name, setName] = useState(project.name)
  const [start, setStart] = useState(project.start.slice(0, 16))
  const [calendar, setCalendar] = useState(project.calendar)
  const [minutesPerDay, setMinutesPerDay] = useState(String(project.minutesPerDay))
  const [statusDate, setStatusDate] = useState(project.statusDate?.slice(0, 16) ?? '')

  function save() {
    const patch: Record<string, unknown> = {}
    if (name.trim() !== '' && name !== project.name) patch.name = name.trim()
    if (start !== project.start.slice(0, 16)) patch.start = start + ':00'
    if (calendar !== project.calendar) patch.calendar = calendar
    if (Number(minutesPerDay) !== project.minutesPerDay) patch.minutesPerDay = Number(minutesPerDay)
    const currentStatus = project.statusDate?.slice(0, 16) ?? ''
    if (statusDate !== currentStatus) {
      if (statusDate === '') patch.clearStatusDate = true
      else patch.statusDate = statusDate + ':00'
    }
    if (Object.keys(patch).length > 0) onCommands([{ op: 'setProject', ...patch }])
    onClose()
  }

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
        aria-label="Project settings"
        tabIndex={-1}
        ref={(element) => element?.focus()}
        onClick={(event) => event.stopPropagation()}
      >
        <h3>Project settings</h3>
        <label className="inspector-row">
          <span className="inspector-label">Name</span>
          <input value={name} readOnly={!editable} onChange={(event) => setName(event.target.value)} />
        </label>
        <label className="inspector-row">
          <span className="inspector-label">Start</span>
          <input type="datetime-local" value={start} readOnly={!editable} onChange={(event) => setStart(event.target.value)} />
        </label>
        <label className="inspector-row">
          <span className="inspector-label">Calendar</span>
          <select value={calendar} disabled={!editable} onChange={(event) => setCalendar(event.target.value)}>
            {project.calendars.map((c) => (
              <option key={c}>{c}</option>
            ))}
          </select>
        </label>
        <label className="inspector-row">
          <span className="inspector-label">Minutes/day</span>
          <input value={minutesPerDay} readOnly={!editable} onChange={(event) => setMinutesPerDay(event.target.value)} />
        </label>
        <label className="inspector-row">
          <span className="inspector-label">Status date</span>
          <input type="datetime-local" value={statusDate} readOnly={!editable} onChange={(event) => setStatusDate(event.target.value)} />
        </label>
        <div className="modal-actions">
          <button onClick={onClose}>Cancel</button>
          {editable && (
            <button className="primary" onClick={save}>
              Save
            </button>
          )}
        </div>
      </div>
    </div>
  )
}
