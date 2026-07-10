import { useCallback, useEffect, useState } from 'react'
import type { ApiClient } from '../api/client'
import type { ProjectInfo } from '../api/types'
import { dateTime } from '../lib/format'

interface Props {
  client: ApiClient
  onOpen: (project: ProjectInfo) => void
}

export function ProjectList({ client, onOpen }: Props) {
  const [projects, setProjects] = useState<ProjectInfo[] | null>(null)
  const [name, setName] = useState('')
  const [error, setError] = useState<string | null>(null)

  const refresh = useCallback(async () => {
    try {
      setProjects(await client.listProjects())
      setError(null)
    } catch (cause) {
      setError(cause instanceof Error ? cause.message : String(cause))
    }
  }, [client])

  useEffect(() => {
    void refresh()
  }, [refresh])

  async function create(event: React.FormEvent) {
    event.preventDefault()
    if (name.trim() === '') return
    try {
      const created = await client.createProject(name.trim())
      setName('')
      onOpen(created)
    } catch (cause) {
      setError(cause instanceof Error ? cause.message : String(cause))
    }
  }

  async function remove(project: ProjectInfo) {
    if (!window.confirm(`Delete project '${project.name}'? This cannot be undone.`)) return
    try {
      await client.deleteProject(project.id)
      await refresh()
    } catch (cause) {
      setError(cause instanceof Error ? cause.message : String(cause))
    }
  }

  return (
    <div className="page">
      <div className="page-header">
        <h2>Projects</h2>
        <form className="inline-form" onSubmit={create}>
          <input
            value={name}
            onChange={(event) => setName(event.target.value)}
            placeholder="New project name"
            aria-label="New project name"
          />
          <button type="submit">Create</button>
        </form>
      </div>
      {error !== null && <p className="error">{error}</p>}
      {projects === null ? (
        <p className="muted">Loading…</p>
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
                <td>{project.lock ? project.lock.userId + (project.lock.stale ? ' (stale)' : '') : ''}</td>
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
