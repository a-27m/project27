import { useCallback, useEffect, useMemo, useRef, useState } from 'react'
import type { ApiClient } from '../api/client'
import type { CalendarDayCell, CalendarDetail, CalendarMonth, Command, ScheduleProject } from '../api/types'
import {
  dayOfMonthOf,
  formatDateRange,
  hoursTotal,
  isWorkingValue,
  leadingBlanksForMonth,
  monthCellStyle,
  monthLabel,
  parseHours,
  type RecurrenceEndMode,
  type RecurrenceKind,
  recurrenceSummary,
  resolvedWeeklyValue,
  weekdayKeyOf,
  weeklyDayIsHighlighted,
  weeklyDayTag,
} from '../lib/calendarManager'
import { useToast } from './toastContext'

const WEEKDAYS = ['monday', 'tuesday', 'wednesday', 'thursday', 'friday', 'saturday', 'sunday'] as const
const WEEKDAY_LABELS = ['Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat', 'Sun']
const STANDARD_HOURS = '08:00-12:00,13:00-17:00'

function todayIso(): string {
  return new Date().toISOString().slice(0, 10)
}

type Tab = 'weekly' | 'exceptions' | 'workweeks'

interface Props {
  client: ApiClient
  projectId: string
  project: ScheduleProject
  editable: boolean
  onCommands: (commands: Command[]) => Promise<void>
  onClose: () => void
}

export function CalendarManager({ client, projectId, project, editable, onCommands, onClose }: Props) {
  const { showError } = useToast()
  const dialogRef = useRef<HTMLDivElement>(null)
  useEffect(() => {
    dialogRef.current?.focus()
  }, [])

  const [selected, setSelected] = useState(project.calendar)
  useEffect(() => {
    if (!project.calendars.includes(selected)) setSelected(project.calendar)
  }, [project.calendars, project.calendar, selected])

  const [detail, setDetail] = useState<CalendarDetail | null>(null)
  const [detailError, setDetailError] = useState(false)
  const [baseDetail, setBaseDetail] = useState<CalendarDetail | null>(null)

  const refreshDetail = useCallback(async () => {
    try {
      const result = await client.calendarDetail(projectId, selected)
      setDetail(result)
      setDetailError(false)
      if (result.base !== null) {
        try {
          setBaseDetail(await client.calendarDetail(projectId, result.base))
        } catch {
          setBaseDetail(null)
        }
      } else {
        setBaseDetail(null)
      }
    } catch (cause) {
      setDetail(null)
      setDetailError(true)
      showError(cause)
    }
  }, [client, projectId, selected, showError])

  useEffect(() => {
    setDetail(null)
    setDetailError(false)
    void refreshDetail()
    // refreshDetail depends on `selected`; re-running it on `selected` change is the point.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [client, projectId, selected])

  const today = useMemo(() => new Date(), [])
  const [monthYear, setMonthYear] = useState(today.getFullYear())
  const [monthMonth, setMonthMonth] = useState(today.getMonth() + 1)
  const [month, setMonth] = useState<CalendarMonth | null>(null)

  const refreshMonth = useCallback(async () => {
    try {
      setMonth(await client.calendarMonth(projectId, selected, monthYear, monthMonth))
    } catch (cause) {
      setMonth(null)
      showError(cause)
    }
  }, [client, projectId, selected, monthYear, monthMonth, showError])

  useEffect(() => {
    void refreshMonth()
  }, [refreshMonth])

  const [tab, setTab] = useState<Tab>('exceptions')

  async function mutate(commands: Command[]) {
    try {
      await onCommands(commands)
      await Promise.all([refreshDetail(), refreshMonth()])
    } catch (cause) {
      showError(cause)
    }
  }

  const [newCalName, setNewCalName] = useState('')
  const [newCalBase, setNewCalBase] = useState('')

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
        className="modal wider cal-modal"
        role="dialog"
        aria-modal="true"
        aria-label="Calendar manager"
        tabIndex={-1}
        ref={dialogRef}
        onClick={(event) => event.stopPropagation()}
      >
        <div className="cal-header">
          <div>
            <div className="cal-eyebrow">Project · {project.name}</div>
            <div className="cal-title">Calendar manager</div>
          </div>
          <button type="button" className="icon-btn" aria-label="Close" onClick={onClose}>
            ✕
          </button>
        </div>

        <div className="cal-body">
          <div className="cal-rail">
            <div className="cal-rail-heading">Calendars</div>
            <div className="cal-rail-list">
              {project.calendars.map((name) => (
                <button
                  key={name}
                  type="button"
                  className={`cal-rail-item${name === selected ? ' selected' : ''}`}
                  aria-pressed={name === selected}
                  onClick={() => setSelected(name)}
                >
                  <div className="cal-rail-item-top">
                    <span>{name}</span>
                    {name === project.calendar && <span className="cal-tag teal">project</span>}
                  </div>
                  <div className="cal-rail-item-sub">
                    {name === selected && detail !== null
                      ? detail.base !== null
                        ? `Based on ${detail.base}`
                        : 'Standalone'
                      : ' '}
                  </div>
                </button>
              ))}
            </div>
            {editable && (
              <form
                className="cal-add-form"
                onSubmit={(event) => {
                  event.preventDefault()
                  if (newCalName.trim() === '') return
                  void mutate([
                    { op: 'addCalendar', name: newCalName.trim(), ...(newCalBase !== '' ? { baseCalendar: newCalBase } : {}) },
                  ])
                  setNewCalName('')
                  setNewCalBase('')
                }}
              >
                <div className="cal-rail-heading">Add calendar</div>
                <input value={newCalName} onChange={(event) => setNewCalName(event.target.value)} placeholder="Name" aria-label="New calendar name" />
                <select value={newCalBase} onChange={(event) => setNewCalBase(event.target.value)} aria-label="New calendar base">
                  <option value="">Standalone (no base)</option>
                  {project.calendars.map((name) => (
                    <option key={name} value={name}>
                      Based on {name}
                    </option>
                  ))}
                </select>
                <button type="submit" className="primary">
                  Add calendar
                </button>
              </form>
            )}
          </div>

          <div className="cal-detail">
            {detailError ? (
              <div className="cal-pane-pad">
                <p className="muted">Couldn't load this calendar.</p>
                <button type="button" onClick={() => void refreshDetail()}>
                  Retry
                </button>
              </div>
            ) : detail === null ? (
              <div className="cal-pane-pad">
                <div className="cal-skeleton" />
                <div className="cal-skeleton" style={{ width: '85%' }} />
                <div className="cal-skeleton" style={{ width: '45%' }} />
              </div>
            ) : (
              <>
                <DetailHeader
                  project={project}
                  detail={detail}
                  editable={editable}
                  onRebase={(base) => void mutate([{ op: 'setCalendarBase', calendar: selected, baseCalendar: base }])}
                />

                <div className="cal-tabs">
                  <button type="button" className={`cal-tab${tab === 'weekly' ? ' active' : ''}`} onClick={() => setTab('weekly')}>
                    Weekly pattern
                  </button>
                  <button type="button" className={`cal-tab${tab === 'exceptions' ? ' active' : ''}`} onClick={() => setTab('exceptions')}>
                    Exceptions <span className="mono">{detail.exceptions.length}</span>
                  </button>
                  <button type="button" className={`cal-tab${tab === 'workweeks' ? ' active' : ''}`} onClick={() => setTab('workweeks')}>
                    Work weeks <span className="mono">{detail.workWeeks.length}</span>
                  </button>
                </div>

                <div className="cal-tab-area">
                  <div className="cal-tab-body">
                    {tab === 'weekly' && (
                      <WeeklyPatternTab
                        detail={detail}
                        baseDetail={baseDetail}
                        editable={editable}
                        onSetDay={(day, value) => {
                          const trimmed = value.trim().toLowerCase()
                          if (trimmed === '' || trimmed === 'inherit') {
                            void mutate([{ op: 'setCalendarDay', calendar: selected, day }])
                          } else if (trimmed === 'off') {
                            void mutate([{ op: 'setCalendarDay', calendar: selected, day, off: true }])
                          } else {
                            const parsed = parseHours(trimmed)
                            if (!parsed.ok) {
                              showError(new Error(parsed.error ?? 'Invalid hours.'))
                              return
                            }
                            void mutate([
                              {
                                op: 'setCalendarDay',
                                calendar: selected,
                                day,
                                intervals: parsed.intervals.map((interval) => {
                                  const [start, end] = interval.split('-')
                                  return { start, end }
                                }),
                              },
                            ])
                          }
                        }}
                      />
                    )}
                    {tab === 'exceptions' && (
                      <ExceptionsTab
                        calendar={selected}
                        detail={detail}
                        editable={editable}
                        onAdd={(commands) => void mutate(commands)}
                        onRemove={(name) => void mutate([{ op: 'removeCalendarException', calendar: selected, name }])}
                      />
                    )}
                    {tab === 'workweeks' && (
                      <WorkWeeksTab
                        calendar={selected}
                        detail={detail}
                        editable={editable}
                        onAdd={(commands) => void mutate(commands)}
                        onRemove={(name) => void mutate([{ op: 'removeWorkWeek', calendar: selected, name }])}
                      />
                    )}
                  </div>
                  <MonthPreview
                    year={monthYear}
                    month={monthMonth}
                    data={month}
                    onPrev={() => {
                      if (monthMonth === 1) {
                        setMonthYear((y) => y - 1)
                        setMonthMonth(12)
                      } else {
                        setMonthMonth((m) => m - 1)
                      }
                    }}
                    onNext={() => {
                      if (monthMonth === 12) {
                        setMonthYear((y) => y + 1)
                        setMonthMonth(1)
                      } else {
                        setMonthMonth((m) => m + 1)
                      }
                    }}
                  />
                </div>
              </>
            )}

            <div className="cal-footer">
              <div className="muted small">Changes apply immediately and re-schedule affected tasks.</div>
              <div className="cal-footer-actions">
                {editable && detail !== null && (
                  <button
                    type="button"
                    className="danger"
                    disabled={detail.isProjectDefault || detail.directTaskCount > 0 || detail.directResourceCount > 0}
                    title={
                      detail.isProjectDefault
                        ? 'The project calendar cannot be removed.'
                        : detail.directTaskCount > 0 || detail.directResourceCount > 0
                          ? 'Used by tasks or resources — reassign them first.'
                          : undefined
                    }
                    onClick={() => {
                      if (!window.confirm(`Remove calendar "${selected}"?`)) return
                      void mutate([{ op: 'removeCalendar', calendar: selected }])
                    }}
                  >
                    Remove calendar
                  </button>
                )}
                <button type="button" onClick={onClose}>
                  Close
                </button>
              </div>
            </div>
          </div>
        </div>
      </div>
    </div>
  )
}

function DetailHeader({
  project,
  detail,
  editable,
  onRebase,
}: {
  project: ScheduleProject
  detail: CalendarDetail
  editable: boolean
  onRebase: (base: string | null) => void
}) {
  const usage = detail.isProjectDefault
    ? `Project calendar · ${detail.directTaskCount} task(s) directly assigned · ${detail.directResourceCount} resource(s)`
    : detail.directTaskCount + detail.directResourceCount === 0
      ? 'Not assigned to any task or resource'
      : `Assigned to ${detail.directTaskCount} task(s) · ${detail.directResourceCount} resource(s)`

  const note =
    detail.base !== null
      ? `Days left as inherit follow ${detail.base}. Re-basing keeps days set here and re-resolves the rest against the new base — exceptions and work weeks are untouched.`
      : 'Standalone: nothing is inherited, so every inherit day resolves to off. Pick a base to have unset days follow another calendar.'

  return (
    <div className="cal-detail-header">
      <div className="cal-detail-top">
        <div>
          <div className="cal-detail-name">{detail.name}</div>
          <div className="muted small">{usage}</div>
        </div>
        <label className="cal-base-select">
          <span className="cal-field-label">Base calendar</span>
          <select
            value={detail.base ?? ''}
            disabled={!editable}
            onChange={(event) => onRebase(event.target.value === '' ? null : event.target.value)}
          >
            <option value="">Standalone (no base)</option>
            {project.calendars
              .filter((name) => name !== detail.name)
              .map((name) => (
                <option key={name} value={name}>
                  Based on {name}
                </option>
              ))}
          </select>
        </label>
      </div>
      <div className={`cal-note${detail.base !== null ? ' info' : ' plain'}`}>{note}</div>
    </div>
  )
}

function WeeklyPatternTab({
  detail,
  baseDetail,
  editable,
  onSetDay,
}: {
  detail: CalendarDetail
  baseDetail: CalendarDetail | null
  editable: boolean
  onSetDay: (day: (typeof WEEKDAYS)[number], value: string) => void
}) {
  const hasBase = detail.base !== null
  const allSet = WEEKDAYS.every((day) => detail.weekly[day] !== 'inherit')
  const workingDays = WEEKDAYS.filter((day) => isWorkingValue(resolvedWeeklyValue(detail.weekly[day], baseDetail?.weekly[day], hasBase))).length

  return (
    <div className="cal-weekly">
      <div className="cal-section-head">
        <div className="cal-section-title">Weekly pattern</div>
        <div className="muted small">
          Type <span className="mono">off</span>, <span className="mono">inherit</span>, or an interval list.
        </div>
      </div>
      <div className="cal-weekly-table">
        {WEEKDAYS.map((day, index) => {
          const value = detail.weekly[day]
          const tag = weeklyDayTag(hasBase, value, baseDetail?.weekly[day], allSet)
          return (
            <WeeklyRow
              key={day}
              label={WEEKDAY_LABELS[index]}
              value={value}
              tag={tag}
              highlighted={weeklyDayIsHighlighted(tag)}
              editable={editable}
              onCommit={(next) => onSetDay(day, next)}
              onReset={() => onSetDay(day, 'inherit')}
            />
          )
        })}
      </div>
      <div className="muted small">Effective week: {workingDays} working days.</div>
    </div>
  )
}

const WEEKLY_TAG_LABEL: Record<string, string> = {
  inherited: 'inherited',
  overridesBase: 'overrides base',
  sameAsBase: 'same as base',
  default: 'default',
  setHere: 'set here',
  ownHours: 'own hours',
}

function WeeklyRow({
  label,
  value,
  tag,
  highlighted,
  editable,
  onCommit,
  onReset,
}: {
  label: string
  value: string
  tag: string
  highlighted: boolean
  editable: boolean
  onCommit: (value: string) => void
  onReset: () => void
}) {
  const [draft, setDraft] = useState<string | null>(null)
  const commit = () => {
    if (draft !== null && draft !== value) onCommit(draft)
    setDraft(null)
  }
  return (
    <div className={`cal-weekly-row${highlighted ? ' highlighted' : ''}`}>
      <div className="cal-weekly-day">{label}</div>
      <input
        className="mono"
        value={draft ?? value}
        readOnly={!editable}
        onChange={(event) => setDraft(event.target.value)}
        onBlur={commit}
        onKeyDown={(event) => {
          if (event.key === 'Enter') commit()
          else if (event.key === 'Escape') setDraft(null)
        }}
        aria-label={`${label} working hours`}
      />
      <span className={`cal-tag ${highlighted ? 'blue' : 'gray'}`}>{WEEKLY_TAG_LABEL[tag] ?? tag}</span>
      {editable && (
        <button type="button" className="icon-btn" title="Reset to inherit" aria-label="Reset day to inherit" onClick={onReset}>
          ↺
        </button>
      )}
    </div>
  )
}

interface ExceptionFormState {
  name: string
  start: string
  end: string
  mode: 'off' | 'hours'
  hoursText: string
  recOn: boolean
  recKind: RecurrenceKind
  recInterval: string
  /** Weekly only: which weekdays it recurs on. Defaults to the From date's weekday but is user-editable. */
  recDays: string[]
  /** Monthly only: day of month (1-31) it recurs on, as text. Defaults to the From date's day but is user-editable. */
  recDay: string
  endMode: RecurrenceEndMode
  endValue: string
}

function newExceptionForm(): ExceptionFormState {
  const start = todayIso()
  return {
    name: '',
    start,
    end: '',
    mode: 'off',
    hoursText: STANDARD_HOURS,
    recOn: false,
    recKind: 'monthly',
    recInterval: '1',
    recDays: [weekdayKeyOf(start)],
    recDay: String(dayOfMonthOf(start)),
    endMode: 'never',
    endValue: '',
  }
}

function ExceptionsTab({
  calendar,
  detail,
  editable,
  onAdd,
  onRemove,
}: {
  calendar: string
  detail: CalendarDetail
  editable: boolean
  onAdd: (commands: Command[]) => void
  onRemove: (name: string) => void
}) {
  const [open, setOpen] = useState(false)
  const [form, setForm] = useState<ExceptionFormState>(newExceptionForm)
  const [nameError, setNameError] = useState<string | null>(null)
  const [hoursError, setHoursError] = useState<string | null>(null)
  const [recurrenceError, setRecurrenceError] = useState<string | null>(null)

  function submit() {
    const name = form.name.trim()
    if (name === '') {
      setNameError('Give the exception a name — it shows up in the schedule log.')
      return
    }
    setNameError(null)

    let intervals: { start: string; end: string }[] | undefined
    if (form.mode === 'hours') {
      const parsed = parseHours(form.hoursText)
      if (!parsed.ok) {
        setHoursError(parsed.error)
        return
      }
      setHoursError(null)
      intervals = parsed.intervals.map((interval) => {
        const [start, end] = interval.split('-')
        return { start, end }
      })
    } else {
      setHoursError(null)
    }

    if (form.recOn && form.recKind === 'weekly' && form.recDays.length === 0) {
      setRecurrenceError('Pick at least one weekday for a weekly recurrence.')
      return
    }
    const monthDay = Number(form.recDay)
    if (form.recOn && form.recKind === 'monthly' && (!Number.isInteger(monthDay) || monthDay < 1 || monthDay > 31)) {
      setRecurrenceError('Day of month must be between 1 and 31.')
      return
    }
    setRecurrenceError(null)

    const every = Math.max(1, Number(form.recInterval) || 1)
    const recurrence = form.recOn
      ? form.recKind === 'daily'
        ? { kind: 'daily' as const, every }
        : form.recKind === 'weekly'
          ? { kind: 'weekly' as const, every, days: form.recDays }
          : { kind: 'monthlyDay' as const, every, day: monthDay }
      : undefined

    const commands: Command[] = [
      {
        op: 'addCalendarException',
        calendar,
        name,
        from: form.start,
        ...(!form.recOn && form.end !== '' ? { to: form.end } : {}),
        ...(form.recOn && form.endMode === 'date' && form.endValue.trim() !== '' ? { to: form.endValue.trim() } : {}),
        ...(intervals !== undefined ? { intervals } : {}),
        ...(recurrence !== undefined ? { recurrence } : {}),
        ...(form.recOn && form.endMode === 'count' && form.endValue.trim() !== '' ? { times: Number(form.endValue.trim()) } : {}),
      },
    ]
    onAdd(commands)
    setForm(newExceptionForm())
    setOpen(false)
  }

  return (
    <div className="cal-exceptions">
      <div className="cal-section-head">
        <div className="cal-section-title">Exceptions</div>
        <div className="muted small">Dated overrides layered on the weekly pattern.</div>
      </div>

      {detail.exceptions.length === 0 ? (
        <div className="cal-empty">
          <div className="cal-empty-title">No exceptions on this calendar</div>
          <div className="muted small">
            Company holidays, a shutdown week, a half-day. Exceptions are <strong>not</strong> inherited from{' '}
            {detail.base ?? 'a base calendar'}
            {detail.base === null ? '' : ' — add them here if this calendar needs them'}.
          </div>
        </div>
      ) : (
        <div className="cal-table">
          <div className="cal-table-head cal-exception-row">
            <div>Name</div>
            <div>Dates</div>
            <div>Hours</div>
            <div>Repeats</div>
            <div />
          </div>
          {detail.exceptions.map((exception) => (
            <div key={exception.name} className="cal-table-row cal-exception-row">
              <div>{exception.name}</div>
              <div className="mono">{formatDateRange(exception.start, exception.end)}</div>
              <div className="cal-hours-cell">
                <span className={`cal-dot ${exception.dayOff ? 'error' : 'success'}`} />
                <span className="mono">{exception.dayOff ? 'off' : exception.intervals.join(',')}</span>
              </div>
              <div>
                <span className={`cal-tag ${exception.recurrence !== null ? 'blue' : 'gray'}`}>
                  {exception.recurrence !== null ? exception.recurrence.kind : 'once'}
                </span>
              </div>
              {editable ? (
                <button type="button" className="cal-remove-btn" title="Remove exception" aria-label="Remove exception" onClick={() => onRemove(exception.name)}>
                  ✕
                </button>
              ) : (
                <div />
              )}
            </div>
          ))}
        </div>
      )}

      {editable &&
        (open ? (
          <div className="cal-add-box">
            <div className="cal-eyebrow">New exception</div>
            <div className="cal-form-row3">
              <label>
                <span className="cal-field-label">Name</span>
                <input value={form.name} placeholder="Independence Day" onChange={(event) => setForm({ ...form, name: event.target.value })} />
              </label>
              <label>
                <span className="cal-field-label">From</span>
                <input type="date" value={form.start} onChange={(event) => setForm({ ...form, start: event.target.value })} />
              </label>
              {!form.recOn && (
                <label>
                  <span className="cal-field-label">To (optional)</span>
                  <input type="date" value={form.end} onChange={(event) => setForm({ ...form, end: event.target.value })} />
                </label>
              )}
            </div>
            {nameError !== null && <InlineNotification kind="error" title="Name required" subtitle={nameError} />}

            <div className="cal-working-time">
              <div className="cal-field-label">Working time</div>
              <div className="cal-switcher">
                <button type="button" className={form.mode === 'off' ? 'active' : ''} onClick={() => setForm({ ...form, mode: 'off' })}>
                  Day off
                </button>
                <button type="button" className={form.mode === 'hours' ? 'active' : ''} onClick={() => setForm({ ...form, mode: 'hours' })}>
                  Working hours
                </button>
              </div>
              {form.mode === 'hours' && (
                <div className="cal-hours-box">
                  <input
                    className="mono"
                    value={form.hoursText}
                    placeholder="08:00-12:00,13:00-17:00"
                    onChange={(event) => setForm({ ...form, hoursText: event.target.value })}
                    aria-label="Working intervals"
                  />
                  <HoursChips text={form.hoursText} onChange={(text) => setForm({ ...form, hoursText: text })} />
                  {hoursError !== null && <InlineNotification kind="error" title="Check the hours" subtitle={hoursError} />}
                </div>
              )}
            </div>

            <label className="cal-checkbox">
              <input
                type="checkbox"
                checked={form.recOn}
                onChange={(event) =>
                  setForm({
                    ...form,
                    recOn: event.target.checked,
                    recDays: event.target.checked && form.recDays.length === 0 ? [weekdayKeyOf(form.start)] : form.recDays,
                    recDay: event.target.checked && form.recDay.trim() === '' ? String(dayOfMonthOf(form.start)) : form.recDay,
                  })
                }
              />
              Repeats
            </label>
            {form.recOn && (
              <div className="cal-recurrence-box">
                <label>
                  <span className="cal-field-label">Kind</span>
                  <select
                    value={form.recKind}
                    onChange={(event) => {
                      const kind = event.target.value as RecurrenceKind
                      setForm({
                        ...form,
                        recKind: kind,
                        recDays: kind === 'weekly' && form.recDays.length === 0 ? [weekdayKeyOf(form.start)] : form.recDays,
                        recDay: kind === 'monthly' && form.recDay.trim() === '' ? String(dayOfMonthOf(form.start)) : form.recDay,
                      })
                    }}
                  >
                    <option value="daily">Daily</option>
                    <option value="weekly">Weekly</option>
                    <option value="monthly">Monthly</option>
                  </select>
                </label>
                <label>
                  <span className="cal-field-label">Every</span>
                  <input
                    className="mono"
                    value={form.recInterval}
                    onChange={(event) => setForm({ ...form, recInterval: event.target.value })}
                  />
                </label>
                <div>
                  <span className="cal-field-label">Ends</span>
                  <div className="cal-ends-row">
                    <div className="cal-switcher">
                      {(['never', 'count', 'date'] as const).map((mode) => (
                        <button
                          key={mode}
                          type="button"
                          className={form.endMode === mode ? 'active' : ''}
                          onClick={() => setForm({ ...form, endMode: mode })}
                        >
                          {mode === 'never' ? 'Never' : mode === 'count' ? 'After N' : 'On date'}
                        </button>
                      ))}
                    </div>
                    {form.endMode !== 'never' && (
                      <input
                        className="mono grow"
                        type={form.endMode === 'date' ? 'date' : 'text'}
                        value={form.endValue}
                        onChange={(event) => setForm({ ...form, endValue: event.target.value })}
                        aria-label="Recurrence end value"
                      />
                    )}
                  </div>
                </div>

                {form.recKind === 'weekly' && (
                  <div className="cal-rec-days" role="group" aria-label="Weekdays">
                    <span className="cal-field-label">On</span>
                    <span className="checks">
                      {WEEKDAYS.map((day, index) => (
                        <label key={day}>
                          <input
                            type="checkbox"
                            checked={form.recDays.includes(day)}
                            onChange={(event) =>
                              setForm({
                                ...form,
                                recDays: event.target.checked ? [...form.recDays, day] : form.recDays.filter((d) => d !== day),
                              })
                            }
                          />
                          {WEEKDAY_LABELS[index]}
                        </label>
                      ))}
                    </span>
                  </div>
                )}
                {form.recKind === 'monthly' && (
                  <label className="cal-rec-day-of-month">
                    <span className="cal-field-label">Day of month</span>
                    <input
                      className="mono"
                      value={form.recDay}
                      onChange={(event) => setForm({ ...form, recDay: event.target.value })}
                    />
                  </label>
                )}
                {recurrenceError !== null && <InlineNotification kind="error" title="Check the recurrence" subtitle={recurrenceError} />}

                <div className="cal-recurrence-summary mono">
                  {recurrenceSummary(form.recKind, Math.max(1, Number(form.recInterval) || 1), form.endMode, form.endValue, {
                    days: form.recDays,
                    day: Number(form.recDay) || undefined,
                  })}
                </div>
              </div>
            )}

            <div className="cal-form-actions">
              <button type="button" className="primary" onClick={submit}>
                Add exception
              </button>
              <button
                type="button"
                onClick={() => {
                  setOpen(false)
                  setForm(newExceptionForm())
                  setNameError(null)
                  setHoursError(null)
                  setRecurrenceError(null)
                }}
              >
                Cancel
              </button>
            </div>
          </div>
        ) : (
          <button type="button" className="tertiary" onClick={() => setOpen(true)}>
            ＋ Add exception
          </button>
        ))}
    </div>
  )
}

function HoursChips({ text, onChange }: { text: string; onChange: (text: string) => void }) {
  const parsed = parseHours(text)
  const intervals = parsed.ok ? parsed.intervals : []
  return (
    <div className="cal-chip-row">
      {intervals.map((interval, index) => (
        <span key={`${interval}-${index}`} className="cal-chip mono">
          {interval}
          <button
            type="button"
            aria-label={`Remove ${interval}`}
            onClick={() => onChange(intervals.filter((_, i) => i !== index).join(','))}
          >
            ×
          </button>
        </span>
      ))}
      <button type="button" className="cal-chip-add mono" onClick={() => onChange([...intervals, '18:00-20:00'].join(','))}>
        ＋ interval
      </button>
      {intervals.length > 0 && <span className="muted small">{hoursTotal(intervals)}h</span>}
    </div>
  )
}

function InlineNotification({ kind, title, subtitle }: { kind: 'error' | 'info'; title: string; subtitle: string }) {
  return (
    <div className={`cal-notification ${kind}`}>
      <div className="cal-notification-title">{title}</div>
      <div className="cal-notification-subtitle">{subtitle}</div>
    </div>
  )
}

interface WorkWeekFormState {
  name: string
  start: string
  end: string
  days: Record<string, string>
}

function newWorkWeekForm(): WorkWeekFormState {
  return {
    name: '',
    start: todayIso(),
    end: todayIso(),
    days: Object.fromEntries(WEEKDAYS.map((day) => [day, 'inherit'])),
  }
}

function WorkWeeksTab({
  calendar,
  detail,
  editable,
  onAdd,
  onRemove,
}: {
  calendar: string
  detail: CalendarDetail
  editable: boolean
  onAdd: (commands: Command[]) => void
  onRemove: (name: string) => void
}) {
  const [open, setOpen] = useState(false)
  const [form, setForm] = useState<WorkWeekFormState>(newWorkWeekForm)
  const [nameError, setNameError] = useState<string | null>(null)

  function submit() {
    const name = form.name.trim()
    if (name === '') {
      setNameError('Give the work week a name.')
      return
    }
    setNameError(null)

    const days: Record<string, { start: string; end: string }[]> = {}
    for (const day of WEEKDAYS) {
      const value = form.days[day].trim().toLowerCase()
      if (value === '' || value === 'inherit') continue
      if (value === 'off') {
        days[day] = []
        continue
      }
      const parsed = parseHours(value)
      if (!parsed.ok) {
        setNameError(`${day}: ${parsed.error ?? 'invalid hours'}`)
        return
      }
      days[day] = parsed.intervals.map((interval) => {
        const [start, end] = interval.split('-')
        return { start, end }
      })
    }

    onAdd([{ op: 'addWorkWeek', calendar, name, from: form.start, to: form.end, ...(Object.keys(days).length > 0 ? { days } : {}) }])
    setForm(newWorkWeekForm())
    setOpen(false)
  }

  return (
    <div className="cal-workweeks">
      <div className="cal-section-head">
        <div className="cal-section-title">Work weeks</div>
        <div className="muted small">A different weekly pattern for a date range.</div>
      </div>

      {detail.workWeeks.map((week) => (
        <div key={week.name} className="cal-workweek-card">
          <div className="cal-workweek-header">
            <div className="cal-workweek-header-left">
              <span className="cal-workweek-name">{week.name}</span>
              <span className="mono muted">{formatDateRange(week.start, week.end)}</span>
            </div>
            {editable && (
              <button type="button" className="cal-remove-btn" title="Remove work week" aria-label="Remove work week" onClick={() => onRemove(week.name)}>
                ✕
              </button>
            )}
          </div>
          <div className="cal-workweek-grid">
            {WEEKDAYS.map((day, index) => (
              <div key={day} className="cal-workweek-cell">
                <div className="cal-weekday-label">{WEEKDAY_LABELS[index]}</div>
                <div className={`mono cal-workweek-value${week.days[day] === 'off' ? ' error' : week.days[day] === 'inherit' ? ' muted' : ''}`}>
                  {week.days[day]}
                </div>
              </div>
            ))}
          </div>
        </div>
      ))}

      {detail.workWeeks.length === 0 && (
        <div className="cal-empty">
          <div className="cal-empty-title">No work weeks on this calendar</div>
          <div className="muted small">Add one when a stretch of the schedule runs on different hours — summer Fridays, a shutdown week, a crunch month.</div>
        </div>
      )}

      {editable &&
        (open ? (
          <div className="cal-add-box">
            <div className="cal-eyebrow">New work week</div>
            <div className="cal-form-row3">
              <label>
                <span className="cal-field-label">Name</span>
                <input value={form.name} placeholder="Summer hours" onChange={(event) => setForm({ ...form, name: event.target.value })} />
              </label>
              <label>
                <span className="cal-field-label">From</span>
                <input type="date" value={form.start} onChange={(event) => setForm({ ...form, start: event.target.value })} />
              </label>
              <label>
                <span className="cal-field-label">To</span>
                <input type="date" value={form.end} onChange={(event) => setForm({ ...form, end: event.target.value })} />
              </label>
            </div>
            {nameError !== null && <InlineNotification kind="error" title="Check this work week" subtitle={nameError} />}
            <div className="cal-workweek-form-grid">
              {WEEKDAYS.map((day, index) => (
                <label key={day} className="cal-workweek-form-cell">
                  <span className="cal-field-label">{WEEKDAY_LABELS[index]}</span>
                  <input
                    className="mono"
                    value={form.days[day]}
                    placeholder="inherit"
                    onChange={(event) => setForm({ ...form, days: { ...form.days, [day]: event.target.value } })}
                  />
                </label>
              ))}
            </div>
            <div className="cal-form-actions">
              <button type="button" className="primary" onClick={submit}>
                Add work week
              </button>
              <button
                type="button"
                onClick={() => {
                  setOpen(false)
                  setForm(newWorkWeekForm())
                  setNameError(null)
                }}
              >
                Cancel
              </button>
            </div>
          </div>
        ) : (
          <button type="button" className="tertiary" onClick={() => setOpen(true)}>
            ＋ Add work week
          </button>
        ))}
    </div>
  )
}

function MonthPreview({
  year,
  month,
  data,
  onPrev,
  onNext,
}: {
  year: number
  month: number
  data: CalendarMonth | null
  onPrev: () => void
  onNext: () => void
}) {
  const leading = leadingBlanksForMonth(year, month)
  const legend: { label: string; fill: string; textVar: string }[] = [
    { label: 'Working', fill: 'var(--layer-02)', textVar: '--border-subtle-00' },
    { label: 'Off (weekly)', fill: 'transparent', textVar: '--border-subtle-00' },
    { label: 'Exception · off', fill: 'rgba(250,77,86,0.18)', textVar: '--support-error' },
    { label: 'Exception · hours', fill: 'rgba(66,190,101,0.18)', textVar: '--support-success' },
    { label: 'Work-week override', fill: 'rgba(255,131,43,0.16)', textVar: '--support-caution-major' },
  ]
  return (
    <div className="cal-month-preview">
      <div className="cal-month-head">
        <div className="cal-eyebrow">Effective calendar</div>
        <div className="cal-month-nav">
          <button type="button" className="icon-btn" aria-label="Previous month" onClick={onPrev}>
            ‹
          </button>
          <button type="button" className="icon-btn" aria-label="Next month" onClick={onNext}>
            ›
          </button>
        </div>
      </div>
      <div className="cal-month-label">{monthLabel(year, month)}</div>
      <div className="cal-month-grid">
        {WEEKDAY_LABELS.map((label) => (
          <div key={label} className="cal-month-dow">
            {label}
          </div>
        ))}
        {Array.from({ length: leading }).map((_, index) => (
          <div key={`blank-${index}`} />
        ))}
        {data === null
          ? Array.from({ length: 30 }).map((_, index) => <div key={index} className="cal-month-cell cal-skeleton" />)
          : data.days.map((cell) => <MonthCell key={cell.date} cell={cell} />)}
      </div>
      <div className="cal-legend">
        {legend.map((entry) => (
          <div key={entry.label} className="cal-legend-row">
            <span className="cal-legend-swatch" style={{ background: entry.fill, borderColor: `var(${entry.textVar})` }} />
            <span className="muted small">{entry.label}</span>
          </div>
        ))}
      </div>
      <div className="muted small">Precedence: exception, then work week, then the weekly pattern.</div>
    </div>
  )
}

function MonthCell({ cell }: { cell: CalendarDayCell }) {
  const style = monthCellStyle(cell.source, cell.working)
  const day = Number(cell.date.slice(8, 10))
  const sourceLabel = cell.source === 'exception' ? 'exception' : cell.source === 'workWeek' ? 'work week' : 'weekly pattern'
  const title = `${sourceLabel} · ${cell.working ? 'working' : 'off'}${cell.ruleName !== null ? ` — ${cell.ruleName}` : ''}`
  return (
    <div className="cal-month-cell" title={title} style={{ background: style.fill, color: `var(${style.textVar})` }}>
      <div>{day}</div>
      {style.hasMarker && <div className="cal-month-marker" style={{ background: `var(${style.textVar})` }} />}
    </div>
  )
}
