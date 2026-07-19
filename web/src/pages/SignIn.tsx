import { useState } from 'react'
import { ApiClient } from '../api/client'
import { beginSignIn } from '../lib/oidc'
import { useToast } from '../components/toastContext'
import type { Session } from '../state/auth'

interface Props {
  onSignedIn: (session: Session, userName: string) => void
}

/** Sign-in: server URL, then dev user (Development only), a pasted bearer token (manual/testing),
 *  or SSO — redirects to the provider's OIDC authorization endpoint (Authorization Code + PKCE). */
export function SignIn({ onSignedIn }: Props) {
  const [serverUrl, setServerUrl] = useState('')
  const [mode, setMode] = useState<'dev' | 'token' | 'sso'>('dev')
  const [devUser, setDevUser] = useState('alice')
  const [token, setToken] = useState('')
  const [busy, setBusy] = useState(false)
  const { showError } = useToast()

  async function signIn(event: React.FormEvent) {
    event.preventDefault()
    setBusy(true)
    const trimmedServerUrl = serverUrl.trim().replace(/\/+$/, '')
    try {
      if (mode === 'sso') {
        await beginSignIn(trimmedServerUrl)
        return // browser is navigating away
      }
      const session: Session =
        mode === 'dev'
          ? { mode: 'dev', serverUrl: trimmedServerUrl, devUser: devUser.trim() }
          : { mode: 'token', serverUrl: trimmedServerUrl, token: token.trim() }
      const credentials =
        session.mode === 'dev'
          ? { serverUrl: session.serverUrl, devUser: session.devUser }
          : { serverUrl: session.serverUrl, token: session.token }
      const me = await new ApiClient(credentials).me()
      onSignedIn(session, me.name)
    } catch (cause) {
      showError(cause)
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
            <input type="radio" checked={mode === 'sso'} onChange={() => setMode('sso')} />
            Single sign-on
          </label>
          <label>
            <input type="radio" checked={mode === 'dev'} onChange={() => setMode('dev')} />
            Dev user
          </label>
          <label>
            <input type="radio" checked={mode === 'token'} onChange={() => setMode('token')} />
            Bearer token
          </label>
        </div>
        {mode === 'dev' && (
          <label>
            User
            <input value={devUser} onChange={(event) => setDevUser(event.target.value)} />
          </label>
        )}
        {mode === 'token' && (
          <label>
            Token
            <input value={token} onChange={(event) => setToken(event.target.value)} type="password" />
          </label>
        )}
        <button type="submit" disabled={busy}>
          {busy ? 'Signing in…' : mode === 'sso' ? 'Continue' : 'Sign in'}
        </button>
      </form>
    </div>
  )
}
