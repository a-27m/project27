import { useCallback, useEffect, useState } from 'react'
import type { ApiClient } from '../api/client'
import type { ViewResult } from '../api/types'

const TABLES = ['entry', 'schedule', 'cost', 'work', 'tracking', 'variance', 'evm', 'summary'] as const

interface Props {
  client: ApiClient
  projectId: string
  /** Bumps when the schedule changes so the grid refetches. */
  version: number
}

/** Server-evaluated view engine: tables, custom fields, filters, sorts, groups (12p-4). */
export function TableView({ client, projectId, version }: Props) {
  const [table, setTable] = useState<string>('entry')
  const [fields, setFields] = useState('')
  const [filter, setFilter] = useState('')
  const [sort, setSort] = useState('')
  const [groupBy, setGroupBy] = useState('')
  const [result, setResult] = useState<ViewResult | null>(null)
  const [error, setError] = useState<string | null>(null)

  const load = useCallback(() => {
    client
      .view(projectId, { table, fields, filter, sort, groupBy })
      .then((next) => {
        setResult(next)
        setError(null)
      })
      .catch((cause: unknown) => setError(cause instanceof Error ? cause.message : String(cause)))
  }, [client, projectId, table, fields, filter, sort, groupBy])

  useEffect(() => {
    load()
    // `version` is a refresh trigger, not data.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [load, version])

  return (
    <div className="usage">
      <form
        className="usage-controls"
        onSubmit={(event) => {
          event.preventDefault()
          load()
        }}
      >
        <select value={table} aria-label="Table" onChange={(event) => setTable(event.target.value)}>
          {TABLES.map((name) => (
            <option key={name}>{name}</option>
          ))}
        </select>
        <input
          value={fields}
          onChange={(event) => setFields(event.target.value)}
          placeholder="fields: id,name,cost"
          aria-label="Fields"
        />
        <input
          value={filter}
          onChange={(event) => setFilter(event.target.value)}
          placeholder='filter: critical = true and cost > 1000'
          aria-label="Filter"
          className="grow"
        />
        <input value={sort} onChange={(event) => setSort(event.target.value)} placeholder="sort: cost desc" aria-label="Sort" />
        <input
          value={groupBy}
          onChange={(event) => setGroupBy(event.target.value)}
          placeholder="group by"
          aria-label="Group by"
        />
        <button type="submit">Apply</button>
      </form>
      {error !== null && <p className="error pad">{error}</p>}
      {result === null ? (
        <p className="muted pad">Loading…</p>
      ) : (
        <div className="usage-scroll">
          {result.groups.map((group, index) => (
            <div key={group.heading ?? index}>
              {group.heading !== null && <h3 className="table-group">{group.heading}</h3>}
              <table className="usage-table">
                <thead>
                  <tr>
                    {result.fields.map((field) => (
                      <th key={field.key}>{field.caption}</th>
                    ))}
                  </tr>
                </thead>
                <tbody>
                  {group.rows.map((row) => (
                    <tr key={row.uid}>
                      {result.fields.map((field) => {
                        const raw = row.values[field.key]
                        return (
                          <td key={field.key} className={NUMERIC.has(field.kind) ? 'num' : ''}>
                            {formatCell(field.kind, raw)}
                          </td>
                        )
                      })}
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          ))}
        </div>
      )}
    </div>
  )
}

const NUMERIC = new Set(['Number', 'Cost', 'Work', 'Duration', 'Percent', 'WholeNumber'])

function formatCell(kind: string, raw: unknown): string {
  if (raw === null || raw === undefined) return ''
  switch (kind) {
    case 'Duration':
      return `${Math.round((Number(raw) / 480) * 100) / 100}d`
    case 'Work':
      return `${Math.round((Number(raw) / 60) * 100) / 100}h`
    case 'Percent':
      return `${String(raw)}%`
    case 'Date':
      return String(raw).slice(0, 16).replace('T', ' ')
    case 'Flag':
      return raw === true ? 'yes' : 'no'
    case 'Cost':
    case 'Number':
      return String(Math.round(Number(raw) * 100) / 100)
    default:
      return String(raw)
  }
}
