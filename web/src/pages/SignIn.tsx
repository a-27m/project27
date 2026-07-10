import { useState } from 'react'
import { ApiClient, type Credentials } from '../api/client'

interface Props {
  onSignedIn: (credentials: Credentials, userName: string) => void
}

/** Sign-in: server URL plus either a bearer token (OIDC) or a DevAuth user. */
export function SignIn({ onSignedIn }: Props) {
  const [serverUrl, setServerUrl] = useState('')
  const [mode, setMode] = useState<'dev' | 'token'>('dev')
  const [devUser, setDevUser] = useState('alice')
  const [token, setToken] = useState('')
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)

  async function signIn(event: React.FormEvent) {
    event.preventDefault()
    setBusy(true)
    setError(null)
    const credentials: Credentials = {
      serverUrl: serverUrl.trim().replace(/\/+$/, ''),
      token: mode === 'token' ? token.trim() : undefined,
      devUser: mode === 'dev' ? devUser.trim() : undefined,
    }
    try {
      const me = await new ApiClient(credentials).me()
      onSignedIn(credentials, me.name)
    } catch (cause) {
      setError(cause instanceof Error ? cause.message : String(cause))
    } finally {
      setBusy(false)
    }
  }

  return (
    <div className="signin">
      <form className="signin-card" onSubmit={signIn}>
        <h1>Project27</h1>
        <label>
          Server
          <input
            value={serverUrl}
            onChange={(event) => setServerUrl(event.target.value)}
            placeholder="same origin"
            autoFocus
          />
        </label>
        <div className="signin-mode" role="radiogroup" aria-label="Authentication">
          <label>
            <input type="radio" checked={mode === 'dev'} onChange={() => setMode('dev')} />
            Dev user
          </label>
          <label>
            <input type="radio" checked={mode === 'token'} onChange={() => setMode('token')} />
            Bearer token
          </label>
        </div>
        {mode === 'dev' ? (
          <label>
            User
            <input value={devUser} onChange={(event) => setDevUser(event.target.value)} />
          </label>
        ) : (
          <label>
            Token
            <input value={token} onChange={(event) => setToken(event.target.value)} type="password" />
          </label>
        )}
        {error !== null && <p className="error">{error}</p>}
        <button type="submit" disabled={busy}>
          {busy ? 'Signing in…' : 'Sign in'}
        </button>
      </form>
    </div>
  )
}
