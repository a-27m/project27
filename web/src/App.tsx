import { useEffect, useMemo, useState } from 'react'
import { ApiClient, type Credentials } from './api/client'
import { loadCredentials, saveCredentials } from './state/auth'
import { ProjectList } from './pages/ProjectList'
import { ProjectView } from './pages/ProjectView'
import { SignIn } from './pages/SignIn'

type Route = { page: 'list' } | { page: 'project'; id: string }

function routeFromHash(): Route {
  const match = /^#\/p\/([0-9a-fA-F-]{36})$/.exec(window.location.hash)
  return match !== null ? { page: 'project', id: match[1] } : { page: 'list' }
}

export default function App() {
  const [credentials, setCredentials] = useState<Credentials | null>(loadCredentials)
  const [userId, setUserId] = useState<string | null>(null)
  const [route, setRoute] = useState<Route>(routeFromHash)

  const client = useMemo(() => (credentials !== null ? new ApiClient(credentials) : null), [credentials])

  useEffect(() => {
    const onHashChange = () => setRoute(routeFromHash())
    window.addEventListener('hashchange', onHashChange)
    return () => window.removeEventListener('hashchange', onHashChange)
  }, [])

  // Resolve the session's user id (needed for lock-ownership checks).
  useEffect(() => {
    if (client === null) {
      setUserId(null)
      return
    }
    let cancelled = false
    client
      .me()
      .then((me) => {
        if (!cancelled) setUserId(me.id)
      })
      .catch(() => {
        if (!cancelled) {
          setCredentials(null)
          saveCredentials(null)
        }
      })
    return () => {
      cancelled = true
    }
  }, [client])

  if (credentials === null || client === null) {
    return (
      <SignIn
        onSignedIn={(next) => {
          saveCredentials(next)
          setCredentials(next)
        }}
      />
    )
  }

  return (
    <div className="app">
      <header className="app-bar">
        <span className="brand">Project27</span>
        <span className="spacer" />
        <span className="muted">{credentials.devUser ?? userId ?? ''}</span>
        <button
          onClick={() => {
            saveCredentials(null)
            setCredentials(null)
            window.location.hash = ''
          }}
        >
          Sign out
        </button>
      </header>
      {route.page === 'list' ? (
        <ProjectList
          client={client}
          onOpen={(project) => {
            window.location.hash = `#/p/${project.id}`
          }}
        />
      ) : userId === null ? (
        <p className="muted">Loading…</p>
      ) : (
        <ProjectView
          client={client}
          projectId={route.id}
          userId={userId}
          onBack={() => {
            window.location.hash = ''
          }}
        />
      )}
    </div>
  )
}
