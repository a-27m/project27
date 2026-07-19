import { useCallback, useEffect, useState } from 'react'
import type { ApiClient } from '../api/client'
import type { ViewResult } from '../api/types'
import { NUMERIC_FIELD_KINDS, formatFieldValue } from '../lib/format'
import { TABLES } from './tableColumns'

interface Props {
  client: ApiClient
  projectId: string
  /** Bumps when the schedule changes so the grid refetches. */
  version: number
  table: string
  onTableChange: (table: string) => void
  /** Persisted column selection for the active subview; empty = server default. */
  columnKeys: string[]
}

/** Server-evaluated view engine: tables, custom fields, filters, sorts, groups (12p-4). */
export function TableView({ client, projectId, version, table, onTableChange, columnKeys }: Props) {
  const [filter, setFilter] = useState('')
  const [sort, setSort] = useState('')
  const [groupBy, setGroupBy] = useState('')
  const [result, setResult] = useState<ViewResult | null>(null)
  const [error, setError] = useState<string | null>(null)

  const fields = columnKeys.join(',')

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
        <select value={table} aria-label="Table" onChange={(event) => onTableChange(event.target.value)}>
          {TABLES.map((name) => (
            <option key={name}>{name}</option>
          ))}
        </select>
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
        <button type="submit" className="primary">Apply</button>
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
                          <td key={field.key} className={NUMERIC_FIELD_KINDS.has(field.kind) ? 'num' : ''}>
                            {formatFieldValue(field.kind, raw)}
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
