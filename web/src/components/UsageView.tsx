import { useEffect, useState } from 'react'
import type { ApiClient } from '../api/client'
import type { Usage } from '../api/types'
import { useToast } from './toastContext'

interface Props {
  client: ApiClient
  projectId: string
  /** Bumps when the schedule changes so the grid refetches. */
  version: number
}

/** Read-only usage grid: tasks × time buckets (work hours, totals). */
export function UsageView({ client, projectId, version }: Props) {
  const [usage, setUsage] = useState<Usage | null>(null)
  const [granularity, setGranularity] = useState<'day' | 'week'>('week')
  const [showCost, setShowCost] = useState(false)
  const [failed, setFailed] = useState(false)
  const { showError } = useToast()

  useEffect(() => {
    let cancelled = false
    client
      .usage(projectId, granularity)
      .then((result) => {
        if (!cancelled) {
          setUsage(result)
          setFailed(false)
        }
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
  }, [client, projectId, granularity, version, showError])

  if (failed) return <p className="muted pad">Couldn't load usage</p>
  if (usage === null) return <p className="muted pad">Loading…</p>

  const columns = [...new Set(usage.rows.flatMap((row) => row.buckets.map((bucket) => bucket.date)))].sort()

  return (
    <div className="usage">
      <div className="usage-controls">
        <label>
          <input type="radio" checked={granularity === 'week'} onChange={() => setGranularity('week')} /> Weeks
        </label>
        <label>
          <input type="radio" checked={granularity === 'day'} onChange={() => setGranularity('day')} /> Days
        </label>
        <label>
          <input type="checkbox" checked={showCost} onChange={(event) => setShowCost(event.target.checked)} /> Cost
        </label>
      </div>
      <div className="usage-scroll">
        <table className="usage-table">
          <thead>
            <tr>
              <th className="sticky-col">Task</th>
              {columns.map((column) => (
                <th key={column}>{column.slice(5)}</th>
              ))}
              <th>Total</th>
            </tr>
          </thead>
          <tbody>
            {usage.rows.map((row) => (
              <tr key={row.uid} className={row.summary ? 'summary' : ''}>
                <td className="sticky-col" style={{ paddingLeft: 8 + row.outlineLevel * 14 }}>
                  {row.name}
                </td>
                {columns.map((column) => {
                  const bucket = row.buckets.find((candidate) => candidate.date === column)
                  return (
                    <td key={column} className="num">
                      {bucket === undefined
                        ? ''
                        : showCost
                          ? bucket.cost.toFixed(0)
                          : (bucket.workMinutes / 60).toFixed(1) + 'h'}
                    </td>
                  )
                })}
                <td className="num total">
                  {showCost ? row.totalCost.toFixed(0) : (row.totalWorkMinutes / 60).toFixed(1) + 'h'}
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </div>
  )
}
