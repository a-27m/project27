const STORAGE_KEY = 'p27.auth'

/** What's persisted across reloads. OIDC sessions don't need a token here — `oidc-client-ts`
 *  owns that in its own store — just enough to know which server/mode to restore against. */
export type Session =
  | { mode: 'dev'; serverUrl: string; devUser: string }
  | { mode: 'token'; serverUrl: string; token: string }
  | { mode: 'oidc'; serverUrl: string }

export function loadSession(): Session | null {
  try {
    const raw = localStorage.getItem(STORAGE_KEY)
    if (raw === null) return null
    const parsed: unknown = JSON.parse(raw)
    if (typeof parsed === 'object' && parsed !== null && 'serverUrl' in parsed && 'mode' in parsed) {
      return parsed as Session
    }
  } catch {
    /* corrupted storage: sign in again */
  }
  return null
}

export function saveSession(session: Session | null): void {
  if (session === null) localStorage.removeItem(STORAGE_KEY)
  else localStorage.setItem(STORAGE_KEY, JSON.stringify(session))
}
