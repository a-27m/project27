import { UserManager, WebStorageStateStore, type User } from 'oidc-client-ts'

/** `/api/auth/config` — server owns the OIDC provider settings so the SPA never bakes them in at build time. */
export interface AuthConfig {
  devAuth: boolean
  authority: string | null
  clientId: string | null
  scopes: string
}

export async function fetchAuthConfig(serverUrl: string): Promise<AuthConfig> {
  const response = await fetch(`${serverUrl}/api/auth/config`, { headers: { Accept: 'application/json' } })
  if (!response.ok) throw new Error(`cannot load auth config (${response.status})`)
  return (await response.json()) as AuthConfig
}

const CALLBACK_PATH = '/callback'
const SERVER_URL_KEY = 'p27.oidc.serverUrl'

/** Provider-agnostic: any config satisfying `authority` + `clientId` (Authorization Code + PKCE, RFC 7636) works. */
export function createUserManager(config: AuthConfig): UserManager {
  if (config.authority === null || config.clientId === null) {
    throw new Error('server is not configured for OIDC sign-in')
  }
  return new UserManager({
    authority: config.authority,
    client_id: config.clientId,
    redirect_uri: window.location.origin + CALLBACK_PATH,
    post_logout_redirect_uri: window.location.origin + '/',
    response_type: 'code',
    scope: config.scopes,
    automaticSilentRenew: true,
    userStore: new WebStorageStateStore({ store: window.localStorage }),
    stateStore: new WebStorageStateStore({ store: window.sessionStorage }),
  })
}

export function isCallbackPath(): boolean {
  return window.location.pathname === CALLBACK_PATH
}

/** Kicks off the redirect to the provider's authorization endpoint; remembers which server we're signing in to. */
export async function beginSignIn(serverUrl: string): Promise<void> {
  const config = await fetchAuthConfig(serverUrl)
  const userManager = createUserManager(config)
  sessionStorage.setItem(SERVER_URL_KEY, serverUrl)
  await userManager.signinRedirect()
}

/** Completes the redirect back from `/callback`, exchanging the code for tokens. */
export async function completeSignIn(): Promise<{ serverUrl: string; userManager: UserManager; user: User }> {
  const serverUrl = sessionStorage.getItem(SERVER_URL_KEY)
  if (serverUrl === null) throw new Error('no sign-in in progress')
  sessionStorage.removeItem(SERVER_URL_KEY)
  const config = await fetchAuthConfig(serverUrl)
  const userManager = createUserManager(config)
  const user = await userManager.signinRedirectCallback()
  return { serverUrl, userManager, user }
}

/** Restores a still-valid session on page load without re-prompting the user. */
export async function restoreSession(serverUrl: string): Promise<{ userManager: UserManager; user: User } | null> {
  const config = await fetchAuthConfig(serverUrl)
  if (config.authority === null || config.clientId === null) return null
  const userManager = createUserManager(config)
  const user = await userManager.getUser()
  if (user === null || user.expired) return null
  return { userManager, user }
}

/** Refresh-token grant when the provider issued one (rotation via `automaticSilentRenew`); falls back to a
 *  full redirect through the provider's own session when it didn't (or renewal fails for any reason). */
export function watchForExpiry(userManager: UserManager, onExpired: () => void): () => void {
  const fallbackToRedirect = () => {
    void userManager.signinRedirect().catch(onExpired)
  }
  userManager.events.addSilentRenewError(fallbackToRedirect)
  userManager.events.addUserSignedOut(onExpired)
  return () => {
    userManager.events.removeSilentRenewError(fallbackToRedirect)
    userManager.events.removeUserSignedOut(onExpired)
  }
}

export function signOut(userManager: UserManager): Promise<void> {
  return userManager.signoutRedirect()
}
