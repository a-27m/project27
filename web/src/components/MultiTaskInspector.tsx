import type { ScheduleProject, ScheduleTask } from '../api/types'
import { Icon } from './icons/Icon'

const PERCENT_STOPS = [0, 25, 50, 75, 100] as const

interface Props {
  tasks: ScheduleTask[]
  project: ScheduleProject
  editable: boolean
  onSetPercent: (percent: number) => void
  onAssign: (resource: string) => void
  onIndent: () => void
  onOutdent: () => void
  onDelete: () => void
  onClose: () => void
  onCollapse: () => void
}

/** Bulk editor for a multi-task selection: percent complete, resource assignment, structure (12p-2 restore). */
export function MultiTaskInspector({ tasks, project, editable, onSetPercent, onAssign, onIndent, onOutdent, onDelete, onClose, onCollapse }: Props) {
  const leaves = tasks.filter((task) => !task.summary)

  return (
    <aside
      className="inspector"
      aria-label={`${tasks.length} tasks selected`}
      onKeyDown={(event) => {
        if (event.key === 'Escape') onClose()
      }}
    >
      <header className="inspector-head">
        <strong>{tasks.length} tasks selected</strong>
        <span className="spacer" />
        <button className="inspector-collapse" onClick={onCollapse} title="Collapse inspector" aria-label="Collapse inspector">
          <Icon name="ChevronRight" size={14} />
        </button>
        <button onClick={onClose} aria-label="Close inspector">
          <Icon name="Close" size={14} />
        </button>
      </header>
      <div className="inspector-body">
        <div className="accordion-section">
          <div className="accordion-body">
            <div className="inspector-row">
              <span className="inspector-label">Structure</span>
              <span className="action-group" role="group" aria-label="Structure">
                <button className="icon-btn" disabled={!editable} onClick={onOutdent} title="Outdent (Alt+Shift+←)" aria-label="Outdent">
                  <Icon name="ArrowRight" size={12} style={{ transform: 'scaleX(-1)' }} />
                </button>
                <button className="icon-btn" disabled={!editable} onClick={onIndent} title="Indent (Alt+Shift+→)" aria-label="Indent">
                  <Icon name="ArrowRight" size={12} />
                </button>
                <button className="icon-btn danger" disabled={!editable} onClick={onDelete} title="Delete (Del)">
                  <Icon name="Close" size={12} />
                  Delete
                </button>
              </span>
            </div>

            {leaves.length > 0 && (
              <div className="inspector-row">
                <span className="inspector-label">Set % done</span>
                <span className="action-group" role="group" aria-label="Set % complete for selection">
                  {PERCENT_STOPS.map((stop) => (
                    <button
                      key={stop}
                      className="icon-btn"
                      disabled={!editable}
                      onClick={() => onSetPercent(stop)}
                      title={`Set ${stop}% complete on ${leaves.length} selected task(s)`}
                    >
                      {stop}%
                    </button>
                  ))}
                </span>
              </div>
            )}

            {leaves.length > 0 && editable && project.resources.length > 0 && (
              <div className="inspector-row">
                <span className="inspector-label">Assign</span>
                <select
                  className="menu"
                  aria-label="Assign resource to selection"
                  value=""
                  onChange={(event) => {
                    const resource = event.target.value
                    event.target.value = ''
                    if (resource !== '') onAssign(resource)
                  }}
                >
                  <option value="">Assign to {leaves.length} selected…</option>
                  {project.resources.map((resource) => (
                    <option key={resource.uid} value={resource.name}>
                      {resource.name}
                    </option>
                  ))}
                </select>
              </div>
            )}
          </div>
        </div>
      </div>
    </aside>
  )
}
