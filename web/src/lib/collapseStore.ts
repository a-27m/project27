// Browser-local persistence of which summary tasks are collapsed, per project.
// View state only — never sent to the server (cf. theme.ts). Malformed or absent
// storage yields an empty set so a bad value can never break the sheet.

const keyFor = (projectId: string) => `p27.collapsed.${projectId}`

export function loadCollapsed(projectId: string): Set<number> {
  try {
    const stored = localStorage.getItem(keyFor(projectId))
    if (stored === null) return new Set()
    const parsed: unknown = JSON.parse(stored)
    if (!Array.isArray(parsed)) return new Set()
    return new Set(parsed.filter((uid): uid is number => typeof uid === 'number'))
  } catch {
    return new Set()
  }
}

export function saveCollapsed(projectId: string, collapsed: ReadonlySet<number>): void {
  try {
    if (collapsed.size === 0) localStorage.removeItem(keyFor(projectId))
    else localStorage.setItem(keyFor(projectId), JSON.stringify([...collapsed]))
  } catch {
    // Storage disabled/full: collapse simply won't persist across reloads.
  }
}
