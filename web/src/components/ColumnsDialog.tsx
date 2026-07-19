import { useEffect, useMemo, useRef } from 'react'

export interface ColumnOption {
  key: string
  label: string
  /** Logical section heading (e.g. "Schedule", "Custom Fields"); ungrouped options render as one flat list. */
  group?: string
}

interface Props {
  title: string
  options: readonly ColumnOption[]
  selectedKeys: readonly string[]
  /** Column that can never be unchecked (e.g. Name). */
  mandatoryKey?: string
  /** Column keys to restore when "Reset" is pressed. */
  defaultKeys: readonly string[]
  onChange: (keys: string[]) => void
  onClose: () => void
}

/** Generic column picker, shared by Gantt, Resources, and each Table subview. */
export function ColumnsDialog({ title, options, selectedKeys, mandatoryKey, defaultKeys, onChange, onClose }: Props) {
  const dialogRef = useRef<HTMLDivElement>(null)
  useEffect(() => {
    dialogRef.current?.focus()
  }, [])

  // Group in first-seen order; ungrouped options (no `group` on any option) render flat, no headers.
  const sections = useMemo(() => {
    const grouped = options.some((o) => o.group !== undefined)
    if (!grouped) return [{ heading: null as string | null, options }]
    const order: string[] = []
    const byGroup = new Map<string, ColumnOption[]>()
    for (const option of options) {
      const heading = option.group ?? 'Other'
      if (!byGroup.has(heading)) {
        order.push(heading)
        byGroup.set(heading, [])
      }
      byGroup.get(heading)!.push(option)
    }
    return order.map((heading) => ({ heading, options: byGroup.get(heading)! }))
  }, [options])

  return (
    <div
      className="modal-backdrop"
      role="presentation"
      onClick={onClose}
      onKeyDown={(event) => {
        if (event.key === 'Escape') onClose()
      }}
    >
      <div
        className="modal"
        role="dialog"
        aria-modal="true"
        aria-label={title}
        tabIndex={-1}
        ref={dialogRef}
        onClick={(event) => event.stopPropagation()}
      >
        <h3>{title}</h3>
        {sections.map((section) => (
          <div key={section.heading ?? '_'} className="column-group">
            {section.heading !== null && <h4 className="column-group-heading">{section.heading}</h4>}
            <div className="checks column-checks">
              {section.options.map((option) => (
                <label key={option.key}>
                  <input
                    type="checkbox"
                    checked={selectedKeys.includes(option.key)}
                    disabled={option.key === mandatoryKey}
                    onChange={(event) =>
                      onChange(
                        event.target.checked
                          ? options.filter((o) => selectedKeys.includes(o.key) || o.key === option.key).map((o) => o.key)
                          : selectedKeys.filter((key) => key !== option.key),
                      )
                    }
                  />
                  {option.label}
                </label>
              ))}
            </div>
          </div>
        ))}
        <div className="modal-actions">
          <button style={{ marginRight: 'auto' }} onClick={() => onChange([...defaultKeys])}>
            Reset
          </button>
          <button onClick={onClose}>Close</button>
        </div>
      </div>
    </div>
  )
}
