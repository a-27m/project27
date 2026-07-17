import { useState } from 'react'
import type { Command, ScheduleProject } from '../api/types'
import { dateOnly, durationDays } from '../lib/format'
import { AccordionSection, DateField, SelectField, TextField } from './InspectorFields'
import { Icon } from './icons/Icon'

interface Props {
  project: ScheduleProject
  editable: boolean
  onCommands: (commands: Command[]) => void
  onClose: () => void
  onCollapse: () => void
  onOpenCalendars: () => void
  onOpenCustomFields: () => void
}

type Section = 'overview' | 'schedule' | 'calendars' | 'custom'

/** Project-scope docked inspector (opened via the top-bar project name button). */
export function ProjectInspector({ project, editable, onCommands, onClose, onCollapse, onOpenCalendars, onOpenCustomFields }: Props) {
  const [openSections, setOpenSections] = useState<ReadonlySet<Section>>(new Set(['overview']))
  const isOpen = (section: Section) => openSections.has(section)
  const toggle = (section: Section) =>
    setOpenSections((current) => {
      const next = new Set(current)
      if (next.has(section)) next.delete(section)
      else next.add(section)
      return next
    })
  const set = (patch: Record<string, unknown>) => onCommands([{ op: 'setProject', ...patch }])
  const { stats } = project

  return (
    <aside
      className="inspector"
      aria-label="Project details"
      onKeyDown={(event) => {
        if (event.key === 'Escape') onClose()
      }}
    >
      <header className="inspector-head">
        <strong>{project.name}</strong>
        <span className="spacer" />
        <button className="inspector-collapse" onClick={onCollapse} title="Collapse inspector" aria-label="Collapse inspector">
          <Icon name="ChevronRight" size={14} />
        </button>
        <button onClick={onClose} aria-label="Close inspector">
          <Icon name="Close" size={14} />
        </button>
      </header>
      <div className="inspector-body">
        <AccordionSection title="Overview" open={isOpen('overview')} onToggle={() => toggle('overview')}>
          <StatsTable
            heading={['', 'START', 'FINISH']}
            rows={[
              ['Current', dateOnly(stats.start.current), dateOnly(stats.finish.current)],
              ['Baseline', dateOnly(stats.start.baseline), dateOnly(stats.finish.baseline)],
              ['Actual', dateOnly(stats.start.actual), dateOnly(stats.finish.actual)],
              [
                'Variance',
                stats.start.varianceMinutes === null ? '' : durationDays(stats.start.varianceMinutes, project.minutesPerDay),
                stats.finish.varianceMinutes === null ? '' : durationDays(stats.finish.varianceMinutes, project.minutesPerDay),
              ],
            ]}
            dangerRow={3}
            dangerWhen={(stats.finish.varianceMinutes ?? 0) > 0}
          />
          <StatsTable
            heading={['', 'DUR', 'WORK', 'COST']}
            rows={[
              [
                'Current',
                durationDays(stats.duration.current, project.minutesPerDay),
                durationDays(stats.work.current, 60),
                money(stats.cost.current),
              ],
              [
                'Baseline',
                stats.duration.baseline === null ? '' : durationDays(stats.duration.baseline, project.minutesPerDay),
                stats.work.baseline === null ? '' : durationDays(stats.work.baseline, 60),
                stats.cost.baseline === null ? '' : money(stats.cost.baseline),
              ],
              [
                'Actual',
                '',
                stats.work.actual === null ? '' : durationDays(stats.work.actual, 60),
                stats.cost.actual === null ? '' : money(stats.cost.actual),
              ],
              [
                'Remaining',
                stats.duration.remaining === null ? '' : durationDays(stats.duration.remaining, project.minutesPerDay),
                stats.work.remaining === null ? '' : durationDays(stats.work.remaining, 60),
                stats.cost.remaining === null ? '' : money(stats.cost.remaining),
              ],
            ]}
          />
          <div className="inspector-stats-footer">
            <span>
              % complete · Duration <b>{stats.percentCompleteByDuration}%</b>
            </span>
            <span>
              Work <b>{stats.percentCompleteByWork}%</b>
            </span>
          </div>
        </AccordionSection>

        <AccordionSection title="Schedule settings" open={isOpen('schedule')} onToggle={() => toggle('schedule')}>
          <TextField label="Name" value={project.name} editable={editable} onCommit={(v) => set({ name: v })} />
          <DateField label="Project start" value={project.start} editable={editable} onCommit={(v) => v !== null && set({ start: v })} />
          <DateField
            label="Status date"
            value={project.statusDate}
            editable={editable}
            onCommit={(v) => (v === null ? set({ clearStatusDate: true }) : set({ statusDate: v }))}
          />
          <SelectField
            label="Default calendar"
            value={project.calendar}
            options={project.calendars}
            editable={editable}
            onCommit={(v) => set({ calendar: v })}
          />
          <TextField
            label="Hours/day"
            value={String(project.minutesPerDay / 60)}
            editable={editable}
            onCommit={(v) => set({ minutesPerDay: Number(v) * 60 })}
          />
        </AccordionSection>

        <AccordionSection title="Calendars" hint={`${project.calendars.length}`} open={isOpen('calendars')} onToggle={() => toggle('calendars')}>
          {project.calendars.map((calendar) => (
            <div className="inspector-row" key={calendar}>
              <span className="inspector-value">{calendar}</span>
            </div>
          ))}
          <button className="inspector-manage-btn" onClick={onOpenCalendars}>
            Manage calendars…
          </button>
        </AccordionSection>

        <AccordionSection
          title="Custom fields"
          hint={project.customFields.length > 0 ? String(project.customFields.length) : undefined}
          open={isOpen('custom')}
          onToggle={() => toggle('custom')}
        >
          {project.customFields.length === 0 && <p className="muted">No custom fields defined.</p>}
          {project.customFields.map((field) => (
            <div className="inspector-row" key={field.id}>
              <span className="inspector-value">{field.alias ?? field.id}</span>
              <span className="muted">{field.kind}</span>
            </div>
          ))}
          <button className="inspector-manage-btn" onClick={onOpenCustomFields}>
            Manage custom fields…
          </button>
        </AccordionSection>
      </div>
    </aside>
  )
}

function money(value: number): string {
  return String(Math.round(value * 100) / 100)
}

function StatsTable({
  heading,
  rows,
  dangerRow,
  dangerWhen,
}: {
  heading: string[]
  rows: string[][]
  dangerRow?: number
  dangerWhen?: boolean
}) {
  return (
    <table className="inspector-stats-table">
      <thead>
        <tr>
          {heading.map((cell, index) => (
            <th key={index} className={index === 0 ? '' : 'num'}>
              {cell}
            </th>
          ))}
        </tr>
      </thead>
      <tbody>
        {rows.map((row, rowIndex) => (
          <tr key={rowIndex} className={rowIndex === dangerRow && dangerWhen === true ? 'danger' : ''}>
            {row.map((cell, cellIndex) => (
              <td key={cellIndex} className={cellIndex === 0 ? '' : 'num'}>
                {cell}
              </td>
            ))}
          </tr>
        ))}
      </tbody>
    </table>
  )
}
