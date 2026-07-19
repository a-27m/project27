import { useEffect, useRef, useState } from 'react'
import type { ApiClient } from '../api/client'
import type { Command, ScheduleProject, SnapshotInfo } from '../api/types'
import { useToast } from './toastContext'

// Custom-field and calendar management dialogs + the recurring-task dialog (12p-4).

export function CustomFieldsManager({
  project,
  editable,
  onCommands,
  onClose,
}: {
  project: ScheduleProject
  editable: boolean
  onCommands: (commands: Command[]) => void
  onClose: () => void
}) {
  const [slot, setSlot] = useState('text1')
  const [alias, setAlias] = useState('')
  const [formula, setFormula] = useState('')
  return (
    <Modal label="Custom fields" onClose={onClose}>
      {project.customFields.length === 0 ? (
        <p className="muted">No custom fields defined.</p>
      ) : (
        <table className="data-table">
          <thead>
            <tr>
              <th>Slot</th>
              <th>Alias</th>
              <th>Formula</th>
              <th />
            </tr>
          </thead>
          <tbody>
            {project.customFields.map((field) => (
              <tr key={field.id}>
                <td>{field.id}</td>
                <td>{field.alias ?? ''}</td>
                <td className="mono">{field.hasFormula ? '(formula)' : ''}</td>
                <td>
                  {editable && (
                    <button className="danger" onClick={() => onCommands([{ op: 'removeCustomField', field: field.id }])}>
                      Remove
                    </button>
                  )}
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      )}
      {editable && (
        <form
          className="inline-form wrap"
          onSubmit={(event) => {
            event.preventDefault()
            onCommands([
              {
                op: 'defineCustomField',
                slot: slot.trim(),
                ...(alias.trim() !== '' ? { alias: alias.trim() } : {}),
                ...(formula.trim() !== '' ? { formula: formula.trim() } : {}),
              },
            ])
            setAlias('')
            setFormula('')
          }}
        >
          <input value={slot} onChange={(event) => setSlot(event.target.value)} aria-label="Slot" placeholder="text1 / number1 / …" />
          <input value={alias} onChange={(event) => setAlias(event.target.value)} aria-label="Alias" placeholder="Alias" />
          <input
            value={formula}
            onChange={(event) => setFormula(event.target.value)}
            aria-label="Formula"
            placeholder='Formula, e.g. IIf([totalSlack] < 1d, 100, 0)'
            className="grow"
          />
          <button type="submit" className="primary">Define</button>
        </form>
      )}
    </Modal>
  )
}

const WEEKDAYS = ['monday', 'tuesday', 'wednesday', 'thursday', 'friday', 'saturday', 'sunday'] as const

export function CalendarManager({
  project,
  editable,
  onCommands,
  onClose,
}: {
  project: ScheduleProject
  editable: boolean
  onCommands: (commands: Command[]) => void
  onClose: () => void
}) {
  const [name, setName] = useState('')
  const [base, setBase] = useState('')
  const [selected, setSelected] = useState(project.calendar)
  const [day, setDay] = useState<(typeof WEEKDAYS)[number]>('saturday')
  const [hours, setHours] = useState('')

  return (
    <Modal label="Calendars" onClose={onClose}>
      <p className="muted">Project calendar: {project.calendar}</p>
      <ul className="plain-list">
        {project.calendars.map((calendar) => (
          <li key={calendar}>
            {calendar}
            {editable && calendar !== project.calendar && (
              <button className="danger" onClick={() => onCommands([{ op: 'removeCalendar', calendar }])}>
                Remove
              </button>
            )}
          </li>
        ))}
      </ul>
      {editable && (
        <>
          <form
            className="inline-form"
            onSubmit={(event) => {
              event.preventDefault()
              if (name.trim() === '') return
              onCommands([
                { op: 'addCalendar', name: name.trim(), ...(base !== '' ? { baseCalendar: base } : {}) },
              ])
              setName('')
            }}
          >
            <input value={name} onChange={(event) => setName(event.target.value)} placeholder="New calendar" aria-label="Calendar name" />
            <select value={base} aria-label="Base calendar" onChange={(event) => setBase(event.target.value)}>
              <option value="">standard preset</option>
              {project.calendars.map((calendar) => (
                <option key={calendar} value={calendar}>
                  base: {calendar}
                </option>
              ))}
            </select>
            <button type="submit" className="primary">Add</button>
          </form>
          <form
            className="inline-form"
            onSubmit={(event) => {
              event.preventDefault()
              const trimmed = hours.trim().toLowerCase()
              onCommands([
                trimmed === 'off'
                  ? { op: 'setCalendarDay', calendar: selected, day, off: true }
                  : trimmed === '' || trimmed === 'inherit'
                    ? { op: 'setCalendarDay', calendar: selected, day }
                    : {
                        op: 'setCalendarDay',
                        calendar: selected,
                        day,
                        intervals: trimmed.split(',').map((part) => {
                          const [start, end] = part.split('-')
                          return { start: start.trim(), end: end.trim() }
                        }),
                      },
              ])
            }}
          >
            <select value={selected} aria-label="Calendar to edit" onChange={(event) => setSelected(event.target.value)}>
              {project.calendars.map((calendar) => (
                <option key={calendar}>{calendar}</option>
              ))}
            </select>
            <select value={day} aria-label="Weekday" onChange={(event) => setDay(event.target.value as typeof day)}>
              {WEEKDAYS.map((weekday) => (
                <option key={weekday}>{weekday}</option>
              ))}
            </select>
            <input
              value={hours}
              onChange={(event) => setHours(event.target.value)}
              placeholder="off | inherit | 08:00-12:00,13:00-17:00"
              aria-label="Working hours"
              className="grow"
            />
            <button type="submit" className="primary">Set day</button>
          </form>
        </>
      )}
    </Modal>
  )
}

export function RecurringTaskDialog({
  editable,
  onCommands,
  onClose,
}: {
  editable: boolean
  onCommands: (commands: Command[]) => void
  onClose: () => void
}) {
  const [name, setName] = useState('')
  const [duration, setDuration] = useState('30m')
  const [kind, setKind] = useState('weekly')
  const [every, setEvery] = useState('1')
  const [days, setDays] = useState<string[]>(['monday'])
  const [from, setFrom] = useState('')
  const [times, setTimes] = useState('10')

  return (
    <Modal label="Recurring task" onClose={onClose}>
      <label className="inspector-row">
        <span className="inspector-label">Name</span>
        <input value={name} onChange={(event) => setName(event.target.value)} />
      </label>
      <label className="inspector-row">
        <span className="inspector-label">Duration</span>
        <input value={duration} onChange={(event) => setDuration(event.target.value)} />
      </label>
      <label className="inspector-row">
        <span className="inspector-label">Repeats</span>
        <select value={kind} onChange={(event) => setKind(event.target.value)}>
          <option value="daily">daily</option>
          <option value="weekly">weekly</option>
          <option value="monthlyDay">monthly (day N)</option>
        </select>
      </label>
      <label className="inspector-row">
        <span className="inspector-label">Every</span>
        <input value={every} onChange={(event) => setEvery(event.target.value)} />
      </label>
      {kind === 'weekly' && (
        <div className="inspector-row" role="group" aria-label="Weekdays">
          <span className="inspector-label">On</span>
          <span className="checks">
            {WEEKDAYS.map((weekday) => (
              <label key={weekday}>
                <input
                  type="checkbox"
                  checked={days.includes(weekday)}
                  onChange={(event) =>
                    setDays(event.target.checked ? [...days, weekday] : days.filter((d) => d !== weekday))
                  }
                />
                {weekday.slice(0, 2)}
              </label>
            ))}
          </span>
        </div>
      )}
      <label className="inspector-row">
        <span className="inspector-label">From</span>
        <input type="date" value={from} onChange={(event) => setFrom(event.target.value)} />
      </label>
      <label className="inspector-row">
        <span className="inspector-label">Occurrences</span>
        <input value={times} onChange={(event) => setTimes(event.target.value)} />
      </label>
      <div className="modal-actions">
        <button onClick={onClose}>Cancel</button>
        <button
          className="primary"
          disabled={!editable || name.trim() === '' || from === ''}
          onClick={() => {
            onCommands([
              {
                op: 'addRecurringTask',
                name: name.trim(),
                duration,
                recurrence: {
                  kind: kind === 'monthlyDay' ? 'monthlyDay' : kind,
                  every: Number(every) || 1,
                  ...(kind === 'weekly' ? { days } : {}),
                  ...(kind === 'monthlyDay' ? { day: 1 } : {}),
                },
                from,
                times: Number(times) || 1,
              },
            ])
            onClose()
          }}
        >
          Create
        </button>
      </div>
    </Modal>
  )
}

export function HistoryDialog({
  client,
  projectId,
  editable,
  onReverted,
  onClose,
}: {
  client: ApiClient
  projectId: string
  editable: boolean
  onReverted: () => void
  onClose: () => void
}) {
  const [history, setHistory] = useState<SnapshotInfo[] | null>(null)
  const [failed, setFailed] = useState(false)
  const { showError } = useToast()

  useEffect(() => {
    let cancelled = false
    client
      .history(projectId)
      .then((result) => {
        if (!cancelled) setHistory(result)
      })
      .catch((cause: unknown) => {
        if (!cancelled) {
          setFailed(true)
          showError(cause)
        }
      })
    return () => {
      cancelled = true
    }
  }, [client, projectId, showError])

  return (
    <Modal label="Version history" onClose={onClose}>
      {failed ? (
        <p className="muted">Couldn't load version history</p>
      ) : history === null ? (
        <p className="muted">Loading…</p>
      ) : (
        <table className="data-table">
          <thead>
            <tr>
              <th>Version</th>
              <th>Label</th>
              <th>By</th>
              <th>When</th>
              <th />
            </tr>
          </thead>
          <tbody>
            {history.map((snapshot, index) => (
              <tr key={snapshot.version}>
                <td>
                  v{snapshot.version}
                  {index === 0 ? ' (current)' : ''}
                </td>
                <td>{snapshot.label ?? ''}</td>
                <td>{snapshot.savedByName}</td>
                <td>{snapshot.savedAt.slice(0, 16).replace('T', ' ')}</td>
                <td>
                  {editable && index > 0 && (
                    <button
                      onClick={() => {
                        if (!window.confirm(`Revert the plan to v${snapshot.version}? The current state stays in history.`)) return
                        client
                          .revert(projectId, snapshot.version)
                          .then(() => {
                            onReverted()
                            onClose()
                          })
                          .catch((cause: unknown) => showError(cause))
                      }}
                    >
                      Revert to this
                    </button>
                  )}
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      )}
      {!editable && <p className="muted">Check the project out to revert.</p>}
    </Modal>
  )
}

function Modal({ label, onClose, children }: { label: string; onClose: () => void; children: React.ReactNode }) {
  const dialogRef = useRef<HTMLDivElement>(null)
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
        className="modal wide"
        role="dialog"
        aria-modal="true"
        aria-label={label}
        tabIndex={-1}
        ref={dialogRef}
        onClick={(event) => event.stopPropagation()}
      >
        <h3>{label}</h3>
        {children}
        <div className="modal-actions">
          <button onClick={onClose}>Close</button>
        </div>
      </div>
    </div>
  )
}
