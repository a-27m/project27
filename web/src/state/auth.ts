import type { Credentials } from '../api/client'

const STORAGE_KEY = 'p27.auth'

export function loadCredentials(): Credentials | null {
  try {
    const raw = localStorage.getItem(STORAGE_KEY)
    if (raw === null) return null
    const parsed: unknown = JSON.parse(raw)
    if (typeof parsed === 'object' && parsed !== null && 'serverUrl' in parsed) {
      return parsed as Credentials
    }
  } catch {
    /* corrupted storage: sign in again */
  }
  return null
}

export function saveCredentials(credentials: Credentials | null): void {
  if (credentials === null) localStorage.removeItem(STORAGE_KEY)
  else localStorage.setItem(STORAGE_KEY, JSON.stringify(credentials))
}
