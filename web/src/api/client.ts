import type { Checkout, Command, CommandsResponse, Me, ProjectEvent, ProjectInfo, Schedule, SnapshotInfo, TaskDriver, Usage, VersionInfo, ViewResult } from './types'

export interface Credentials {
  /** Server base URL; empty string = same origin (Vite dev proxy). */
  serverUrl: string
  /** Static bearer token (manual/testing entry). Ignored when `getToken` is set. */
  token?: string
  /** Live token source for OIDC sessions, re-read on every request so refresh/rotation is picked up. */
  getToken?: () => Promise<string | null>
  devUser?: string
}

export class ApiError extends Error {
  readonly status: number

  constructor(status: number, message: string) {
    super(message)
    this.status = status
  }
}

/** Thin fetch wrapper: auth headers on every call, problem-detail errors. */
export class ApiClient {
  private readonly credentials: Credentials

  constructor(credentials: Credentials) {
    this.credentials = credentials
  }

  me(): Promise<Me> {
    return this.request('GET', '/api/me')
  }

  version(): Promise<VersionInfo> {
    return this.request('GET', '/api/version')
  }

  listProjects(): Promise<ProjectInfo[]> {
    return this.request('GET', '/api/projects')
  }

  createProject(name: string, start?: string): Promise<ProjectInfo> {
    return this.request('POST', '/api/projects', { name, start: start ?? null })
  }

  deleteProject(id: string): Promise<void> {
    return this.request('DELETE', `/api/projects/${id}`)
  }

  getProject(id: string): Promise<ProjectInfo> {
    return this.request('GET', `/api/projects/${id}`)
  }

  schedule(id: string): Promise<Schedule> {
    return this.request('GET', `/api/projects/${id}/schedule`)
  }

  async reportHtml(id: string, name: string): Promise<string> {
    const response = await fetch(`${this.credentials.serverUrl}/api/projects/${id}/reports/${name}`, {
      headers: await this.headers(),
    })
    if (!response.ok) throw new ApiError(response.status, await problemDetail(response))
    return response.text()
  }

  view(id: string, params: { table?: string; fields?: string; filter?: string; sort?: string; groupBy?: string }): Promise<ViewResult> {
    const query = new URLSearchParams()
    for (const [key, value] of Object.entries(params)) {
      if (value !== undefined && value !== '') query.set(key, value)
    }
    return this.request('GET', `/api/projects/${id}/view?${query.toString()}`)
  }

  drivers(id: string, uid: number): Promise<TaskDriver[]> {
    return this.request('GET', `/api/projects/${id}/drivers/${uid}`)
  }

  usage(id: string, granularity: 'day' | 'week'): Promise<Usage> {
    return this.request('GET', `/api/projects/${id}/usage?granularity=${granularity}`)
  }

  history(id: string): Promise<SnapshotInfo[]> {
    return this.request('GET', `/api/projects/${id}/history`)
  }

  labelVersion(id: string, label: string, version?: number): Promise<void> {
    return this.request('POST', `/api/projects/${id}/label`, { label, version: version ?? null })
  }

  revert(id: string, version: number, label?: string): Promise<CommandsResponse> {
    return this.request('POST', `/api/projects/${id}/revert`, { version, label: label ?? null })
  }

  importMspdi(xml: string): Promise<ProjectInfo> {
    return this.request('POST', '/api/projects/import/mspdi', undefined, xml)
  }

  /** Fetches a binary/text endpoint with auth and triggers a browser download. */
  async download(path: string, fallbackName: string): Promise<void> {
    const response = await fetch(this.credentials.serverUrl + path, { headers: await this.headers() })
    if (!response.ok) throw new ApiError(response.status, await problemDetail(response))
    const blob = await response.blob()
    const disposition = response.headers.get('Content-Disposition') ?? ''
    const match = /filename="?([^";]+)"?/.exec(disposition)
    const anchor = document.createElement('a')
    anchor.href = URL.createObjectURL(blob)
    anchor.download = match?.[1] ?? fallbackName
    anchor.click()
    setTimeout(() => URL.revokeObjectURL(anchor.href), 60_000)
  }

  checkout(id: string): Promise<Checkout> {
    return this.request('POST', `/api/projects/${id}/checkout`)
  }

  unlock(id: string): Promise<void> {
    return this.request('DELETE', `/api/projects/${id}/lock`)
  }

  commands(id: string, batch: Command[]): Promise<CommandsResponse> {
    return this.request('POST', `/api/projects/${id}/commands`, batch)
  }

  /**
   * Server-sent events via fetch (EventSource cannot carry auth headers).
   * Returns an abort function; invokes onEvent per parsed event.
   */
  subscribe(id: string, onEvent: (event: ProjectEvent) => void): () => void {
    const controller = new AbortController()
    void this.stream(`/api/projects/${id}/events`, controller.signal, onEvent).catch(() => {
      /* subscription ended (abort or network); readers refresh on next action */
    })
    return () => controller.abort()
  }

  private async stream(path: string, signal: AbortSignal, onEvent: (event: ProjectEvent) => void): Promise<void> {
    const response = await fetch(this.credentials.serverUrl + path, { headers: await this.headers(), signal })
    if (!response.ok || response.body === null) return
    const reader = response.body.getReader()
    const decoder = new TextDecoder()
    let buffer = ''
    for (;;) {
      const { done, value } = await reader.read()
      if (done) break
      buffer += decoder.decode(value, { stream: true })
      let separator = buffer.indexOf('\n\n')
      while (separator >= 0) {
        const chunk = buffer.slice(0, separator)
        buffer = buffer.slice(separator + 2)
        const event = parseSse(chunk)
        if (event !== null) onEvent(event)
        separator = buffer.indexOf('\n\n')
      }
    }
  }

  private async headers(): Promise<Record<string, string>> {
    const headers: Record<string, string> = { Accept: 'application/json' }
    const token = this.credentials.getToken ? await this.credentials.getToken() : (this.credentials.token ?? null)
    if (token) headers.Authorization = `Bearer ${token}`
    if (this.credentials.devUser) headers['X-Dev-User'] = this.credentials.devUser
    return headers
  }

  private async request<T>(method: string, path: string, body?: unknown, rawBody?: string): Promise<T> {
    const headers = await this.headers()
    if (body !== undefined) headers['Content-Type'] = 'application/json'
    if (rawBody !== undefined) headers['Content-Type'] = 'application/xml'
    let response: Response
    try {
      response = await fetch(this.credentials.serverUrl + path, {
        method,
        headers,
        body: rawBody ?? (body === undefined ? undefined : JSON.stringify(body)),
      })
    } catch (error) {
      throw new ApiError(0, `cannot reach the server: ${error instanceof Error ? error.message : String(error)}`)
    }
    if (!response.ok) {
      throw new ApiError(response.status, await problemDetail(response))
    }
    if (response.status === 204) return undefined as T
    return (await response.json()) as T
  }
}

async function problemDetail(response: Response): Promise<string> {
  try {
    const body: unknown = await response.json()
    if (typeof body === 'object' && body !== null && 'detail' in body && typeof body.detail === 'string') {
      return body.detail
    }
  } catch {
    /* not a problem document */
  }
  return response.status === 401 ? 'authentication required' : `request failed (${response.status})`
}

/** Parses one SSE chunk ("event: kind\ndata: json"). Exported for tests. */
export function parseSse(chunk: string): ProjectEvent | null {
  let kind: string | null = null
  let data = ''
  for (const line of chunk.split('\n')) {
    if (line.startsWith('event: ')) kind = line.slice(7).trim()
    else if (line.startsWith('data: ')) data += line.slice(6)
  }
  if (kind === null) return null
  let parsed: unknown = null
  try {
    parsed = data === '' ? null : JSON.parse(data)
  } catch {
    parsed = data
  }
  return { kind: kind as ProjectEvent['kind'], data: parsed }
}
