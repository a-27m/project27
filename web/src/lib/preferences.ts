import { useCallback, useEffect, useRef, useState } from 'react'
import type { ApiClient } from '../api/client'
import type { ColumnPreferences } from '../api/types'

const DEBOUNCE_MS = 400

function storageKey(projectId: string): string {
  return `p27.columns.${projectId}`
}

export function loadCachedPreferences(projectId: string): ColumnPreferences {
  try {
    const raw = localStorage.getItem(storageKey(projectId))
    if (raw !== null) return JSON.parse(raw) as ColumnPreferences
  } catch {
    /* corrupted cache: fall through to defaults */
  }
  return {}
}

export function saveCachedPreferences(projectId: string, preferences: ColumnPreferences): void {
  localStorage.setItem(storageKey(projectId), JSON.stringify(preferences))
}

/** Pure merge for one scope's column selection into the wider preferences object. */
export function withGanttColumns(preferences: ColumnPreferences, keys: string[]): ColumnPreferences {
  return { ...preferences, gantt: keys }
}

export function withResourcesColumns(preferences: ColumnPreferences, keys: string[]): ColumnPreferences {
  return { ...preferences, resources: keys }
}

export function withTableColumns(preferences: ColumnPreferences, table: string, keys: string[]): ColumnPreferences {
  return { ...preferences, table: { ...preferences.table, [table]: keys } }
}

export interface ColumnPreferencesApi {
  preferences: ColumnPreferences
  setGanttColumns: (keys: string[]) => void
  setResourcesColumns: (keys: string[]) => void
  setTableColumns: (table: string, keys: string[]) => void
}

/**
 * Server-persisted, per-user column selections (Gantt, Resources, each Table subview).
 * Seeds instantly from a per-project localStorage cache to avoid a flash of defaults,
 * then adopts the server's copy once it loads; writes are optimistic and debounced.
 */
export function useColumnPreferences(client: ApiClient, projectId: string): ColumnPreferencesApi {
  const [preferences, setPreferences] = useState<ColumnPreferences>(() => loadCachedPreferences(projectId))
  const timer = useRef<ReturnType<typeof setTimeout> | null>(null)

  useEffect(() => {
    setPreferences(loadCachedPreferences(projectId))
    client
      .getPreferences(projectId)
      .then((server) => {
        setPreferences(server)
        saveCachedPreferences(projectId, server)
      })
      .catch(() => {
        /* offline or transient error: keep the local cache */
      })
    return () => {
      if (timer.current !== null) clearTimeout(timer.current)
    }
  }, [client, projectId])

  const commit = useCallback(
    (next: ColumnPreferences) => {
      setPreferences(next)
      saveCachedPreferences(projectId, next)
      if (timer.current !== null) clearTimeout(timer.current)
      timer.current = setTimeout(() => {
        void client.setPreferences(projectId, next)
      }, DEBOUNCE_MS)
    },
    [client, projectId],
  )

  return {
    preferences,
    setGanttColumns: (keys) => commit(withGanttColumns(preferences, keys)),
    setResourcesColumns: (keys) => commit(withResourcesColumns(preferences, keys)),
    setTableColumns: (table, keys) => commit(withTableColumns(preferences, table, keys)),
  }
}
