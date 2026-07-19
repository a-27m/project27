import { useCallback, useEffect, useState } from 'react'
import type { ApiClient } from '../api/client'
import type { ProjectInfo } from '../api/types'
import { dateTime } from '../lib/format'
import { useToast } from '../components/toastContext'

interface Props {
  client: ApiClient
  onOpen: (project: ProjectInfo) => void
}

export function ProjectList({ client, onOpen }: Props) {
  const [projects, setProjects] = useState<ProjectInfo[] | null>(null)
  const [name, setName] = useState('')
  const [failed, setFailed] = useState(false)
  const [importing, setImporting] = useState(false)
  const [imageTag, setImageTag] = useState<string | null>(null)
  const { showError } = useToast()

  const refresh = useCallback(async () => {
    try {
      setProjects(await client.listProjects())
      setFailed(false)
    } catch (cause) {
      setFailed(true)
      showError(cause)
    }
  }, [client, showError])

  useEffect(() => {
    void refresh()
  }, [refresh])

  useEffect(() => {
    client
      .version()
      .then((info) => setImageTag(info.imageTag))
      .catch(() => {
        /* cosmetic only; leave blank if unavailable */
      })
  }, [client])

  async function create(event: React.FormEvent) {
    event.preventDefault()
    if (name.trim() === '') return
    try {
      const created = await client.createProject(name.trim())
      setName('')
      onOpen(created)
    } catch (cause) {
      showError(cause)
    }
  }

  async function importMspdi(event: React.ChangeEvent<HTMLInputElement>) {
    const file = event.target.files?.[0]
    event.target.value = ''
    if (!file) return
    setImporting(true)
    try {
      const xml = await file.text()
      const created = await client.importMspdi(xml)
      onOpen(created)
    } catch (cause) {
      showError(cause)
    } finally {
      setImporting(false)
    }
  }

  async function importP27(event: React.ChangeEvent<HTMLInputElement>) {
    const file = event.target.files?.[0]
    event.target.value = ''
    if (!file) return
    setImporting(true)
    try {
      const created = await client.importP27(file)
      onOpen(created)
    } catch (cause) {
      showError(cause)
    } finally {
      setImporting(false)
    }
  }

  async function remove(project: ProjectInfo) {
    if (!window.confirm(`Delete project '${project.name}'? This cannot be undone.`)) return
    try {
      await client.deleteProject(project.id)
      await refresh()
    } catch (cause) {
      showError(cause)
    }
  }

  return (
    <div className="page">
      <div className="page-header">
        <h2>Projects</h2>
        {imageTag !== null && <span className="muted">v{imageTag}</span>}
        <form className="inline-form" onSubmit={create}>
          <input
            value={name}
            onChange={(event) => setName(event.target.value)}
            placeholder="New project name"
            aria-label="New project name"
          />
          <button type="submit" className="primary">Create</button>
        </form>
        <label className="button-like">
          {importing ? 'Importing…' : 'Import MSPDI…'}
          <input
            type="file"
            accept=".xml,application/xml"
            onChange={(event) => void importMspdi(event)}
            disabled={importing}
            hidden
          />
        </label>
        <label className="button-like">
          {importing ? 'Importing…' : 'Import .p27…'}
          <input
            type="file"
            accept=".p27,application/octet-stream"
            onChange={(event) => void importP27(event)}
            disabled={importing}
            hidden
          />
        </label>
      </div>
      {projects === null ? (
        <p className="muted">{failed ? "Couldn't load projects" : 'Loading…'}</p>
      ) : projects.length === 0 ? (
        <p className="muted">No projects yet — create one above.</p>
      ) : (
        <table className="data-table">
          <thead>
            <tr>
              <th>Name</th>
              <th>Role</th>
              <th>Version</th>
              <th>Checked out by</th>
              <th>Created</th>
              <th />
            </tr>
          </thead>
          <tbody>
            {projects.map((project) => (
              <tr key={project.id}>
                <td>
                  <button className="link" onClick={() => onOpen(project)}>
                    {project.name}
                  </button>
                </td>
                <td>{project.role}</td>
                <td>{project.version}</td>
                <td>{project.lock ? project.lock.displayName + (project.lock.stale ? ' (stale)' : '') : ''}</td>
                <td>{dateTime(project.createdAt)}</td>
                <td>
                  {project.role === 'owner' && (
                    <button className="danger" onClick={() => void remove(project)}>
                      Delete
                    </button>
                  )}
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      )}
    </div>
  )
}
