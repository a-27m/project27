import { useState } from 'react'
import type { Command, ScheduleProject } from '../api/types'

interface Props {
  project: ScheduleProject
  editable: boolean
  onCommands: (commands: Command[]) => void
  columnKeys: string[]
}

/** Resource management: list, add, rename, rate, max units, remove (12p-3). */
export function ResourcesView({ project, editable, onCommands, columnKeys }: Props) {
  const [name, setName] = useState('')
  const [type, setType] = useState<'work' | 'material' | 'cost'>('work')
  const [rate, setRate] = useState('')
  const show = (key: string) => columnKeys.includes(key)

  return (
    <div className="page">
      {editable && (
        <form
          className="inline-form"
          onSubmit={(event) => {
            event.preventDefault()
            if (name.trim() === '') return
            onCommands([
              {
                op: 'addResource',
                name: name.trim(),
                type,
                ...(rate.trim() !== '' && type !== 'cost' ? { rate: rate.trim() } : {}),
              },
            ])
            setName('')
            setRate('')
          }}
        >
          <input value={name} onChange={(event) => setName(event.target.value)} placeholder="Resource name" aria-label="Resource name" />
          <select value={type} aria-label="Resource type" onChange={(event) => setType(event.target.value as typeof type)}>
            <option value="work">work</option>
            <option value="material">material</option>
            <option value="cost">cost</option>
          </select>
          <input
            value={rate}
            onChange={(event) => setRate(event.target.value)}
            placeholder={type === 'material' ? 'Rate per unit' : 'Rate, e.g. 50/h'}
            aria-label="Standard rate"
            disabled={type === 'cost'}
          />
          <button type="submit" className="primary">Add resource</button>
        </form>
      )}

      {project.resources.length === 0 ? (
        <p className="muted">No resources yet.</p>
      ) : (
        <table className="data-table">
          <thead>
            <tr>
              {show('name') && <th>Name</th>}
              {show('type') && <th>Type</th>}
              {show('maxUnits') && <th>Max units</th>}
              {show('rate') && <th>Rate</th>}
              {show('initials') && <th>Initials</th>}
              {show('group') && <th>Group</th>}
              {show('calendar') && <th>Calendar</th>}
              {show('materialLabel') && <th>Material label</th>}
              {show('accrual') && <th>Accrual</th>}
              <th />
            </tr>
          </thead>
          <tbody>
            {project.resources.map((resource) => (
              <tr key={resource.uid}>
                {show('name') && (
                  <td>
                    <InlineEdit
                      value={resource.name}
                      editable={editable}
                      label={`Rename ${resource.name}`}
                      onCommit={(v) => onCommands([{ op: 'setResource', resource: resource.name, name: v }])}
                    />
                  </td>
                )}
                {show('type') && <td>{resource.type}</td>}
                {show('maxUnits') && (
                  <td>
                    {resource.type === 'work' ? (
                      <InlineEdit
                        value={`${resource.maxUnits * 100}%`}
                        editable={editable}
                        label={`Max units of ${resource.name}`}
                        onCommit={(v) =>
                          onCommands([
                            {
                              op: 'setResource',
                              resource: resource.name,
                              maxUnits: v.endsWith('%') ? Number(v.slice(0, -1)) / 100 : Number(v),
                            },
                          ])
                        }
                      />
                    ) : (
                      ''
                    )}
                  </td>
                )}
                {show('rate') && (
                  <td>
                    {resource.type !== 'cost' ? (
                      <InlineEdit
                        value={resource.rate}
                        editable={editable}
                        label={`Rate of ${resource.name}`}
                        onCommit={(v) =>
                          onCommands([{ op: 'setResourceRate', resource: resource.name, rate: v }])
                        }
                      />
                    ) : (
                      ''
                    )}
                  </td>
                )}
                {show('initials') && (
                  <td>
                    <InlineEdit
                      value={resource.initials ?? ''}
                      editable={editable}
                      label={`Initials of ${resource.name}`}
                      onCommit={(v) =>
                        onCommands([{ op: 'setResource', resource: resource.name, initials: v || undefined }])
                      }
                    />
                  </td>
                )}
                {show('group') && (
                  <td>
                    <InlineEdit
                      value={resource.group ?? ''}
                      editable={editable}
                      label={`Group of ${resource.name}`}
                      onCommit={(v) =>
                        onCommands([{ op: 'setResource', resource: resource.name, group: v || undefined }])
                      }
                    />
                  </td>
                )}
                {show('calendar') && (
                  <td>
                    {resource.type === 'work' ? (
                      <InlineSelect
                        value={resource.calendar ?? ''}
                        options={['', ...project.calendars]}
                        labels={['(project)', ...project.calendars]}
                        editable={editable}
                        label={`Calendar of ${resource.name}`}
                        onCommit={(v) =>
                          onCommands([
                            v === ''
                              ? { op: 'setResource', resource: resource.name, clearCalendar: true }
                              : { op: 'setResource', resource: resource.name, calendar: v },
                          ])
                        }
                      />
                    ) : (
                      ''
                    )}
                  </td>
                )}
                {show('materialLabel') && (
                  <td>
                    {resource.type === 'material' ? (
                      <InlineEdit
                        value={resource.materialLabel ?? ''}
                        editable={editable}
                        label={`Material label of ${resource.name}`}
                        onCommit={(v) =>
                          onCommands([{ op: 'setResource', resource: resource.name, materialLabel: v || undefined }])
                        }
                      />
                    ) : (
                      ''
                    )}
                  </td>
                )}
                {show('accrual') && (
                  <td>
                    <InlineSelect
                      value={resource.accrual ?? 'prorated'}
                      options={['start', 'prorated', 'end']}
                      labels={['Start', 'Prorated', 'End']}
                      editable={editable}
                      label={`Accrual of ${resource.name}`}
                      onCommit={(v) =>
                        onCommands([
                          {
                            op: 'setResource',
                            resource: resource.name,
                            accrual: v as 'start' | 'prorated' | 'end',
                          },
                        ])
                      }
                    />
                  </td>
                )}
                <td>
                  {editable && (
                    <button
                      className="danger"
                      onClick={() => {
                        if (window.confirm(`Remove '${resource.name}' and its assignments?`)) {
                          onCommands([{ op: 'removeResource', resource: resource.name }])
                        }
                      }}
                    >
                      Remove
                    </button>
                  )}
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      )}
      {!editable && <p className="muted">Check the project out to edit resources.</p>}
    </div>
  )
}

function InlineEdit({
  value,
  editable,
  label,
  onCommit,
}: {
  value: string
  editable: boolean
  label: string
  onCommit: (value: string) => void
}) {
  const [draft, setDraft] = useState<string | null>(null)
  if (!editable) return <>{value}</>
  const commit = () => {
    if (draft !== null && draft.trim() !== '' && draft !== value) onCommit(draft.trim())
    setDraft(null)
  }
  return (
    <input
      className="inline-edit"
      aria-label={label}
      value={draft ?? value}
      onChange={(event) => setDraft(event.target.value)}
      onBlur={commit}
      onKeyDown={(event) => {
        if (event.key === 'Enter') commit()
        else if (event.key === 'Escape') setDraft(null)
      }}
    />
  )
}

function InlineSelect({
  value,
  options,
  labels,
  editable,
  label,
  onCommit,
}: {
  value: string
  options: string[]
  labels: string[]
  editable: boolean
  label: string
  onCommit: (value: string) => void
}) {
  if (!editable) {
    const idx = options.indexOf(value)
    return <>{idx >= 0 ? labels[idx] : value}</>
  }
  return (
    <select
      className="inline-edit"
      aria-label={label}
      value={value}
      onChange={(event) => {
        if (event.target.value !== value) onCommit(event.target.value)
      }}
    >
      {options.map((opt, i) => (
        <option key={opt} value={opt}>
          {labels[i]}
        </option>
      ))}
    </select>
  )
}
