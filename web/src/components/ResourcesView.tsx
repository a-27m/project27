import { useState } from 'react'
import type { Command, ScheduleProject } from '../api/types'

interface Props {
  project: ScheduleProject
  editable: boolean
  onCommands: (commands: Command[]) => void
}

/** Resource management: list, add, rename, rate, max units, remove (12p-3). */
export function ResourcesView({ project, editable, onCommands }: Props) {
  const [name, setName] = useState('')
  const [type, setType] = useState<'work' | 'material' | 'cost'>('work')
  const [rate, setRate] = useState('')

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
          <button type="submit">Add resource</button>
        </form>
      )}

      {project.resources.length === 0 ? (
        <p className="muted">No resources yet.</p>
      ) : (
        <table className="data-table">
          <thead>
            <tr>
              <th>Name</th>
              <th>Type</th>
              <th>Max units</th>
              <th>Rate</th>
              <th />
            </tr>
          </thead>
          <tbody>
            {project.resources.map((resource) => (
              <tr key={resource.uid}>
                <td>
                  <InlineEdit
                    value={resource.name}
                    editable={editable}
                    label={`Rename ${resource.name}`}
                    onCommit={(v) => onCommands([{ op: 'setResource', resource: resource.name, name: v }])}
                  />
                </td>
                <td>{resource.type}</td>
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
