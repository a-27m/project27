import { useEffect, useState } from 'react'
import type { ApiClient } from '../api/client'
import type { Command, ScheduleProject, ScheduleTask, TaskDriver } from '../api/types'
import { dateTime, durationDays, fromWireDate, toWireDate } from '../lib/format'
import { AccordionSection, CheckField, DateField, SelectField, StaticField, TextField } from './InspectorFields'
import { Icon } from './icons/Icon'
import { SegmentedPercent } from './SegmentedPercent'

interface Props {
  task: ScheduleTask
  project: ScheduleProject
  tasks: ScheduleTask[]
  editable: boolean
  client: ApiClient
  projectId: string
  onCommands: (commands: Command[]) => void
  onClose: () => void
  onCollapse: () => void
}

type Section = 'general' | 'advanced' | 'tracking' | 'links' | 'resources' | 'custom' | 'drivers'

const CONSTRAINTS = [
  'asSoonAsPossible',
  'asLateAsPossible',
  'startNoEarlierThan',
  'startNoLaterThan',
  'finishNoEarlierThan',
  'finishNoLaterThan',
  'mustStartOn',
  'mustFinishOn',
] as const

const LINK_TYPES = ['finishToStart', 'startToStart', 'finishToFinish', 'startToFinish'] as const
const CONTOURS = ['flat', 'backLoaded', 'frontLoaded', 'doublePeak', 'earlyPeak', 'latePeak', 'bell', 'turtle'] as const

/** Full-field task editor (docs/spec/12-polish.md parity matrix, 12p-2). */
export function TaskInspector({ task, project, tasks, editable, client, projectId, onCommands, onClose, onCollapse }: Props) {
  const [openSections, setOpenSections] = useState<ReadonlySet<Section>>(new Set(['general']))
  const isOpen = (section: Section) => openSections.has(section)
  const toggle = (section: Section) =>
    setOpenSections((current) => {
      const next = new Set(current)
      if (next.has(section)) next.delete(section)
      else next.add(section)
      return next
    })
  const set = (patch: Record<string, unknown>) => onCommands([{ op: 'setTask', uid: task.uid, ...patch }])
  const rowOf = (uid: number) => tasks.find((t) => t.uid === uid)?.row ?? uid
  const customValuesSet = project.customFields.filter((field) => {
    const raw = task.customValues?.[field.id]
    return raw !== null && raw !== undefined
  }).length

  return (
    <aside
      className="inspector"
      aria-label={`Task ${task.row} details`}
      onKeyDown={(event) => {
        if (event.key === 'Escape') onClose()
      }}
    >
      <header className="inspector-head">
        <strong>
          #{task.row} {task.name}
        </strong>
        <span className="spacer" />
        <button className="inspector-collapse" onClick={onCollapse} title="Collapse inspector" aria-label="Collapse inspector">
          <Icon name="ChevronRight" size={14} />
        </button>
        <button onClick={onClose} aria-label="Close inspector">
          <Icon name="Close" size={14} />
        </button>
      </header>
      <div className="inspector-body">
        <AccordionSection
          title="General"
          hint={task.milestone ? 'Milestone' : durationDays(task.durationMinutes, project.minutesPerDay, task.estimated)}
          open={isOpen('general')}
          onToggle={() => toggle('general')}
        >
          <TextField label="Name" value={task.name} editable={editable} onCommit={(v) => set({ name: v })} />
          <TextField
            label="Duration"
            value={durationDays(task.durationMinutes, project.minutesPerDay, task.estimated)}
            editable={editable && !task.summary}
            onCommit={(v) => set({ duration: v })}
          />
          <StaticField label="Start" value={dateTime(task.start)} />
          <StaticField label="Finish" value={dateTime(task.finish)} />
          <SelectField
            label="Mode"
            value={task.mode}
            options={['auto', 'manual']}
            editable={editable}
            onCommit={(v) => set({ mode: v })}
          />
          <CheckField
            label="Milestone"
            checked={task.milestone}
            editable={editable && !task.summary}
            onCommit={(v) => set({ milestone: v })}
          />
          <CheckField label="Active" checked={task.active} editable={editable} onCommit={(v) => set({ active: v })} />
          <TextField
            label="Priority"
            value={String(task.priority)}
            editable={editable && !task.summary}
            onCommit={(v) => set({ priority: Number(v) })}
          />
          <StaticField label="WBS" value={task.wbs} />
          <StaticField
            label="Slack"
            value={task.totalSlackMinutes === null ? '' : durationDays(task.totalSlackMinutes, project.minutesPerDay)}
          />
        </AccordionSection>

        <AccordionSection title="Advanced" open={isOpen('advanced')} onToggle={() => toggle('advanced')}>
          <SelectField
            label="Type"
            value={task.type}
            options={['fixedUnits', 'fixedDuration', 'fixedWork']}
            editable={editable && !task.summary}
            onCommit={(v) => set({ type: v })}
          />
          <CheckField
            label="Effort-driven"
            checked={task.effortDriven}
            editable={editable && task.type !== 'fixedWork' && !task.summary}
            onCommit={(v) => set({ effortDriven: v })}
          />
          <SelectField
            label="Constraint"
            value={task.constraint}
            options={CONSTRAINTS}
            editable={editable && !task.summary}
            onCommit={(v) =>
              v === 'asSoonAsPossible' || v === 'asLateAsPossible'
                ? set({ constraint: v })
                : set({ constraint: v, constraintDate: task.constraintDate ?? toWireDate(fromWireDate(task.start ?? project.start)) })
            }
          />
          <DateField
            label="Constraint date"
            value={task.constraintDate}
            editable={editable && !task.summary && task.constraint !== 'asSoonAsPossible' && task.constraint !== 'asLateAsPossible'}
            onCommit={(v) => set(v === null ? {} : { constraintDate: v })}
          />
          <DateField
            label="Deadline"
            value={task.deadline}
            editable={editable}
            onCommit={(v) => set(v === null ? { clearDeadline: true } : { deadline: v })}
          />
          <SelectField
            label="Calendar"
            value={task.calendar ?? ''}
            options={['', ...project.calendars]}
            labels={['(project)', ...project.calendars]}
            editable={editable}
            onCommit={(v) => set(v === '' ? { clearCalendar: true } : { calendar: v })}
          />
          <CheckField
            label="Ignore resource calendars"
            checked={task.ignoresResourceCalendars}
            editable={editable}
            onCommit={(v) => set({ ignoreResourceCalendars: v })}
          />
          <TextField
            label="Fixed cost"
            value={String(task.fixedCost)}
            editable={editable}
            onCommit={(v) => set({ fixedCost: Number(v) })}
          />
          <SelectField
            label="Cost accrual"
            value={task.fixedCostAccrual}
            options={['start', 'prorated', 'end']}
            editable={editable}
            onCommit={(v) => set({ fixedCostAccrual: v })}
          />
          <DateField
            label="Manual start"
            value={task.manualStart}
            editable={editable && task.mode === 'manual'}
            onCommit={(v) => set(v === null ? { clearManualStart: true } : { manualStart: v })}
          />
          <DateField
            label="Manual finish"
            value={task.manualFinish}
            editable={editable && task.mode === 'manual'}
            onCommit={(v) => set(v === null ? { clearManualFinish: true } : { manualFinish: v })}
          />
        </AccordionSection>

        <AccordionSection
          title="Tracking"
          hint={`${task.percentComplete}%`}
          open={isOpen('tracking')}
          onToggle={() => toggle('tracking')}
        >
          {!task.summary && (
            <div className="inspector-row">
              <span className="inspector-label">% done</span>
              <SegmentedPercent value={task.percentComplete} editable={editable} onCommit={(v) => set({ percentComplete: v })} />
              <PercentInput value={task.percentComplete} editable={editable} onCommit={(v) => set({ percentComplete: v })} />
            </div>
          )}
          <DateField
            label="Actual start"
            value={task.actualStart}
            editable={editable && !task.summary}
            onCommit={(v) => set(v === null ? { clearActualStart: true } : { actualStart: v })}
          />
          <DateField
            label="Actual finish"
            value={task.actualFinish}
            editable={editable && !task.summary}
            onCommit={(v) => set(v === null ? { clearActualFinish: true } : { actualFinish: v })}
          />
          <StaticField label="Baseline start" value={dateTime(task.baselineStart)} />
          <StaticField label="Baseline finish" value={dateTime(task.baselineFinish)} />
          <StaticField label="Baseline cost" value={task.baselineCost === null ? '' : String(task.baselineCost)} />
          <StaticField
            label="Leveling delay"
            value={task.levelingDelayMinutes > 0 ? durationDays(task.levelingDelayMinutes, project.minutesPerDay) : ''}
          />
          <StaticField label="Cost" value={String(task.cost)} />
          <StaticField label="Work" value={durationDays(task.workMinutes, 60).replace('d', 'h')} />
        </AccordionSection>

        <AccordionSection
          title="Links"
          hint={task.predecessors.length > 0 ? String(task.predecessors.length) : undefined}
          open={isOpen('links')}
          onToggle={() => toggle('links')}
        >
          <LinksSection task={task} tasks={tasks} editable={editable} onCommands={onCommands} rowOf={rowOf} />
        </AccordionSection>

        <AccordionSection
          title="Resources"
          hint={task.assignments.length > 0 ? String(task.assignments.length) : undefined}
          open={isOpen('resources')}
          onToggle={() => toggle('resources')}
        >
          <ResourcesSection task={task} project={project} editable={editable} onCommands={onCommands} />
        </AccordionSection>

        <AccordionSection
          title="Custom"
          hint={project.customFields.length > 0 ? `${customValuesSet}/${project.customFields.length}` : undefined}
          open={isOpen('custom')}
          onToggle={() => toggle('custom')}
        >
          {project.customFields.length === 0 && <p className="muted">No custom fields defined.</p>}
          {project.customFields.map((field) => {
            const raw = task.customValues?.[field.id]
            const text = raw === null || raw === undefined ? '' : String(raw)
            return (
              <TextField
                key={field.id}
                label={(field.alias ?? field.id) + (field.hasFormula ? ' (formula)' : '')}
                value={text}
                editable={editable && !field.hasFormula && !task.summary}
                onCommit={(v) =>
                  set({ customValues: { [field.alias ?? field.id]: v === '' ? null : v } })
                }
              />
            )
          })}
        </AccordionSection>

        <AccordionSection title="Drivers" open={isOpen('drivers')} onToggle={() => toggle('drivers')}>
          <DriversSection client={client} projectId={projectId} uid={task.uid} />
        </AccordionSection>
      </div>
    </aside>
  )
}

function DriversSection({ client, projectId, uid }: { client: ApiClient; projectId: string; uid: number }) {
  const [drivers, setDrivers] = useState<TaskDriver[] | null>(null)
  const [error, setError] = useState<string | null>(null)
  useEffect(() => {
    let cancelled = false
    client
      .drivers(projectId, uid)
      .then((result) => {
        if (!cancelled) setDrivers(result)
      })
      .catch((cause: unknown) => {
        if (!cancelled) setError(cause instanceof Error ? cause.message : String(cause))
      })
    return () => {
      cancelled = true
    }
  }, [client, projectId, uid])
  if (error !== null) return <p className="error">{error}</p>
  if (drivers === null) return <p className="muted">Loading…</p>
  return (
    <ul className="drivers-list">
      {drivers.map((driver, index) => (
        <li key={index} className={driver.binding ? 'binding' : ''}>
          {driver.binding ? '● ' : '○ '}
          {driver.description}
        </li>
      ))}
      <li className="muted">● = binding</li>
    </ul>
  )
}

function LinksSection({
  task,
  tasks,
  editable,
  onCommands,
  rowOf,
}: {
  task: ScheduleTask
  tasks: ScheduleTask[]
  editable: boolean
  onCommands: (commands: Command[]) => void
  rowOf: (uid: number) => number
}) {
  const [newPredecessor, setNewPredecessor] = useState('')
  return (
    <>
      {task.predecessors.length === 0 && <p className="muted">No predecessors.</p>}
      {task.predecessors.map((link) => (
        <div className="inspector-row" key={link.predecessorUid}>
          <span className="inspector-label">#{rowOf(link.predecessorUid)}</span>
          <select
            value={link.type}
            disabled={!editable}
            aria-label="Link type"
            onChange={(event) =>
              onCommands([
                {
                  op: 'setLink',
                  predecessorUid: link.predecessorUid,
                  successorUid: task.uid,
                  type: event.target.value as (typeof LINK_TYPES)[number],
                },
              ])
            }
          >
            {LINK_TYPES.map((type) => (
              <option key={type}>{type}</option>
            ))}
          </select>
          {editable && (
            <button
              className="danger"
              aria-label="Remove link"
              onClick={() => onCommands([{ op: 'unlink', predecessorUid: link.predecessorUid, successorUid: task.uid }])}
            >
              ✕
            </button>
          )}
        </div>
      ))}
      {editable && (
        <div className="inspector-row">
          <input
            placeholder="Predecessor row #"
            aria-label="Predecessor row number"
            value={newPredecessor}
            onChange={(event) => setNewPredecessor(event.target.value)}
          />
          <button
            onClick={() => {
              const predecessor = tasks.find((t) => t.row === Number(newPredecessor))
              if (predecessor !== undefined) {
                onCommands([{ op: 'link', predecessorUid: predecessor.uid, successorUid: task.uid }])
                setNewPredecessor('')
              }
            }}
          >
            Link
          </button>
        </div>
      )}
    </>
  )
}

function ResourcesSection({
  task,
  project,
  editable,
  onCommands,
}: {
  task: ScheduleTask
  project: ScheduleProject
  editable: boolean
  onCommands: (commands: Command[]) => void
}) {
  const [newResource, setNewResource] = useState('')
  const unassigned = project.resources.filter((r) => !task.assignments.some((a) => a.resource === r.name))
  return (
    <>
      {task.assignments.length === 0 && <p className="muted">No resources assigned.</p>}
      {task.assignments.map((assignment) => (
        <div className="inspector-group" key={assignment.resource}>
          <div className="inspector-row">
            <strong>{assignment.resource}</strong>
            <span className="muted">{assignment.resourceType}</span>
            {editable && (
              <button
                className="danger"
                aria-label={`Unassign ${assignment.resource}`}
                onClick={() => onCommands([{ op: 'unassign', uid: task.uid, resource: assignment.resource }])}
              >
                ✕
              </button>
            )}
          </div>
          {assignment.resourceType === 'work' && (
            <>
              <TextField
                label="Units"
                value={`${assignment.units * 100}%`}
                editable={editable}
                onCommit={(v) =>
                  onCommands([
                    {
                      op: 'setAssignment',
                      uid: task.uid,
                      resource: assignment.resource,
                      units: v.endsWith('%') ? Number(v.slice(0, -1)) / 100 : Number(v),
                    },
                  ])
                }
              />
              <TextField
                label="Work"
                value={`${Math.round((assignment.workMinutes / 60) * 100) / 100}h`}
                editable={editable}
                onCommit={(v) => onCommands([{ op: 'setAssignment', uid: task.uid, resource: assignment.resource, work: v }])}
              />
              <SelectField
                label="Contour"
                value={assignment.contour}
                options={CONTOURS}
                editable={editable}
                onCommit={(v) => onCommands([{ op: 'setAssignment', uid: task.uid, resource: assignment.resource, contour: v }])}
              />
            </>
          )}
          {assignment.resourceType === 'cost' && (
            <TextField
              label="Cost"
              value={String(assignment.costInput)}
              editable={editable}
              onCommit={(v) => onCommands([{ op: 'setAssignment', uid: task.uid, resource: assignment.resource, cost: Number(v) }])}
            />
          )}
          <StaticField label="Costed" value={String(assignment.cost)} />
        </div>
      ))}
      {editable && unassigned.length > 0 && (
        <div className="inspector-row">
          <select value={newResource} aria-label="Resource to assign" onChange={(event) => setNewResource(event.target.value)}>
            <option value="">Assign resource…</option>
            {unassigned.map((r) => (
              <option key={r.uid} value={r.name}>
                {r.name} ({r.type})
              </option>
            ))}
          </select>
          <button
            disabled={newResource === ''}
            onClick={() => {
              onCommands([{ op: 'assign', uid: task.uid, resource: newResource }])
              setNewResource('')
            }}
          >
            Assign
          </button>
        </div>
      )}
    </>
  )
}

// ------------------------------------------------------------ field widgets

/** Exact-value input beside the % slider — writes on blur/Enter, same as the slider's onCommit. */
function PercentInput({ value, editable, onCommit }: { value: number; editable: boolean; onCommit: (value: number) => void }) {
  const [draft, setDraft] = useState<string | null>(null)
  const commit = () => {
    if (draft !== null) {
      const parsed = Math.min(100, Math.max(0, Number(draft)))
      if (!Number.isNaN(parsed) && parsed !== value) onCommit(parsed)
    }
    setDraft(null)
  }
  return (
    <input
      value={draft ?? String(value)}
      readOnly={!editable}
      aria-label="Type an exact %"
      title="Type an exact %"
      style={{ width: 44, textAlign: 'right', flex: 'none' }}
      onChange={(event) => setDraft(event.target.value)}
      onBlur={commit}
      onKeyDown={(event) => {
        if (event.key === 'Enter') commit()
        else if (event.key === 'Escape') setDraft(null)
      }}
    />
  )
}

