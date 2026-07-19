import { useEffect, useMemo, useRef, useState } from 'react'
import type { UserManager } from 'oidc-client-ts'
import { ApiClient, type Credentials } from './api/client'
import { loadSession, saveSession, type Session } from './state/auth'
import { completeSignIn, isCallbackPath, restoreSession, signOut as oidcSignOut, watchForExpiry } from './lib/oidc'
import { loadTheme, saveTheme, type Theme } from './lib/theme'
import { Icon } from './components/icons/Icon'
import { useToast } from './components/toastContext'
import { ProjectList } from './pages/ProjectList'
import { ProjectView } from './pages/ProjectView'
import { SignIn } from './pages/SignIn'

type Route = { page: 'list' } | { page: 'project'; id: string }

function routeFromHash(): Route {
  const match = /^#\/p\/([0-9a-fA-F-]{36})$/.exec(window.location.hash)
  return match !== null ? { page: 'project', id: match[1] } : { page: 'list' }
}

function credentialsFor(session: Session, userManager: UserManager | null): Credentials {
  switch (session.mode) {
    case 'dev':
      return { serverUrl: session.serverUrl, devUser: session.devUser }
    case 'token':
      return { serverUrl: session.serverUrl, token: session.token }
    case 'oidc':
      return {
        serverUrl: session.serverUrl,
        getToken: async () => (userManager === null ? null : ((await userManager.getUser())?.access_token ?? null)),
      }
  }
}

export default function App() {
  const [session, setSession] = useState<Session | null>(null)
  const [userManager, setUserManager] = useState<UserManager | null>(null)
  const [resolving, setResolving] = useState(true)
  const [userId, setUserId] = useState<string | null>(null)
  const [userName, setUserName] = useState<string | null>(null)
  const [route, setRoute] = useState<Route>(routeFromHash)
  const [theme, setTheme] = useState<Theme | null>(loadTheme)
  const { showError } = useToast()

  useEffect(() => {
    saveTheme(theme)
  }, [theme])
  // The authorization code is single-use: React StrictMode double-invokes this effect in
  // development, and a second redemption attempt would fail with invalid_grant. Guard it.
  const callbackHandled = useRef(false)

  const credentials = useMemo(() => (session !== null ? credentialsFor(session, userManager) : null), [session, userManager])
  const client = useMemo(() => (credentials !== null ? new ApiClient(credentials) : null), [credentials])

  /** Clears the local session only — no provider round-trip. For internal failure recovery
   *  (an expired/invalid token, a failed `/api/me`): those shouldn't force a full redirect
   *  through the IdP's logout endpoint, just drop back to the sign-in screen. */
  function clearLocalSession() {
    saveSession(null)
    setSession(null)
    setUserManager(null)
    window.location.hash = ''
  }

  /** User-initiated sign-out: also ends the provider session. Bound to the header button only. */
  function signOut() {
    const outgoingUserManager = userManager
    clearLocalSession()
    if (outgoingUserManager !== null) void oidcSignOut(outgoingUserManager)
  }

  // Resolve the initial session: an OIDC redirect callback, or a previously persisted session.
  useEffect(() => {
    let cancelled = false
    async function resolve() {
      if (isCallbackPath()) {
        if (callbackHandled.current) return
        callbackHandled.current = true
        // No `cancelled` short-circuit here: the ref above already guarantees this is the
        // one invocation doing real work, and under StrictMode it's also the one whose
        // `cancelled` flips true before the await settles — bailing on it would skip
        // `setResolving(false)` below and leave the app stuck on the loading screen.
        try {
          const { serverUrl, userManager: manager } = await completeSignIn()
          window.history.replaceState(null, '', '/')
          saveSession({ mode: 'oidc', serverUrl })
          setUserManager(manager)
          setSession({ mode: 'oidc', serverUrl })
        } catch (cause) {
          window.history.replaceState(null, '', '/')
          showError(cause)
        } finally {
          setResolving(false)
        }
        return
      }
      const stored = loadSession()
      if (stored === null) {
        setResolving(false)
        return
      }
      if (stored.mode === 'oidc') {
        const restored = await restoreSession(stored.serverUrl).catch(() => null)
        if (!cancelled) {
          if (restored === null) {
            saveSession(null)
          } else {
            setUserManager(restored.userManager)
            setSession(stored)
          }
        }
      } else {
        setSession(stored)
      }
      if (!cancelled) setResolving(false)
    }
    void resolve()
    return () => {
      cancelled = true
    }
  }, [showError])

  // Refresh-token grant (rotation) when available; falls back to a redirect through the provider's session.
  useEffect(() => {
    if (userManager === null) return
    return watchForExpiry(userManager, clearLocalSession)
  }, [userManager])

  useEffect(() => {
    const onHashChange = () => setRoute(routeFromHash())
    window.addEventListener('hashchange', onHashChange)
    return () => window.removeEventListener('hashchange', onHashChange)
  }, [])

  // Resolve the session's user id (needed for lock-ownership checks) and display name.
  useEffect(() => {
    if (client === null) {
      setUserId(null)
      setUserName(null)
      return
    }
    let cancelled = false
    client
      .me()
      .then((me) => {
        if (!cancelled) {
          setUserId(me.id)
          setUserName(me.name)
        }
      })
      .catch(() => {
        if (!cancelled) clearLocalSession()
      })
    return () => {
      cancelled = true
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [client])

  if (resolving) {
    return <p className="muted">Loading…</p>
  }

  if (session === null || client === null) {
    return (
      <SignIn
        onSignedIn={(next) => {
          saveSession(next)
          setSession(next)
        }}
      />
    )
  }

  const displayName = session.mode === 'dev' ? session.devUser : (userName ?? userId ?? '')
  const dark = theme !== 'light'
  const toggleTheme = () => setTheme(dark ? 'light' : 'dark')

  return (
    <div className="app">
      {route.page === 'list' ? (
        <>
          <header className="app-bar">
            <span className="brand">Project27</span>
            <span className="spacer" />
            <span className="muted">{displayName}</span>
            <button onClick={toggleTheme} title={dark ? 'Switch to light theme' : 'Switch to dark theme'} aria-label="Toggle theme">
              <Icon name="Moon" size={16} />
            </button>
            <button onClick={signOut}>Sign out</button>
          </header>
          <ProjectList
            client={client}
            onOpen={(project) => {
              window.location.hash = `#/p/${project.id}`
            }}
          />
        </>
      ) : userId === null ? (
        <p className="muted">Loading…</p>
      ) : (
        <ProjectView
          client={client}
          projectId={route.id}
          userId={userId}
          userDisplayName={displayName}
          dark={dark}
          onToggleTheme={toggleTheme}
          onSignOut={signOut}
          onBack={() => {
            window.location.hash = ''
          }}
        />
      )}
    </div>
  )
}
