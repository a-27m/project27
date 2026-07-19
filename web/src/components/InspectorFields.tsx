import { useState, type ReactNode } from 'react'
import { Icon } from './icons/Icon'

// Shared accordion + field widgets for the docked inspector (task scope and project scope).

export function AccordionSection({
  title,
  hint,
  open,
  onToggle,
  children,
}: {
  title: string
  hint?: string
  open: boolean
  onToggle: () => void
  children: ReactNode
}) {
  return (
    <div className="accordion-section">
      <button className="accordion-head" onClick={onToggle} aria-expanded={open}>
        <Icon name={open ? 'ChevronUp' : 'ChevronDown'} size={12} />
        <span className="accordion-title">{title}</span>
        <span className="spacer" />
        {hint !== undefined && <span className="accordion-hint">{hint}</span>}
      </button>
      {open && <div className="accordion-body">{children}</div>}
    </div>
  )
}

export function TextField({
  label,
  value,
  editable,
  onCommit,
}: {
  label: string
  value: string
  editable: boolean
  onCommit: (value: string) => void
}) {
  const [draft, setDraft] = useState<string | null>(null)
  const commit = () => {
    if (draft !== null && draft !== value) onCommit(draft)
    setDraft(null)
  }
  return (
    <label className="inspector-row">
      <span className="inspector-label">{label}</span>
      <input
        value={draft ?? value}
        readOnly={!editable}
        onChange={(event) => setDraft(event.target.value)}
        onBlur={commit}
        onKeyDown={(event) => {
          if (event.key === 'Enter') commit()
          else if (event.key === 'Escape') setDraft(null)
        }}
      />
    </label>
  )
}

export function TextAreaField({
  label,
  value,
  editable,
  loading = false,
  maxLength,
  onCommit,
}: {
  label: string
  value: string
  editable: boolean
  /** Shows a placeholder and disables editing until the lazily-fetched value arrives. */
  loading?: boolean
  maxLength?: number
  onCommit: (value: string) => void
}) {
  const [draft, setDraft] = useState<string | null>(null)
  const commit = () => {
    if (draft !== null && draft !== value) onCommit(draft)
    setDraft(null)
  }
  const current = draft ?? value
  return (
    <label className="inspector-row inspector-row-textarea">
      <span className="inspector-label">{label}</span>
      <div className="inspector-textarea-wrap">
        <textarea
          value={loading ? '' : current}
          placeholder={loading ? 'Loading…' : undefined}
          readOnly={!editable || loading}
          maxLength={maxLength}
          rows={4}
          onChange={(event) => setDraft(event.target.value)}
          onBlur={commit}
          onKeyDown={(event) => {
            if (event.key === 'Escape') setDraft(null)
          }}
        />
        {maxLength !== undefined && !loading && (
          <span className="inspector-char-count">
            {current.length}/{maxLength}
          </span>
        )}
      </div>
    </label>
  )
}

export function StaticField({ label, value }: { label: string; value: string }) {
  return (
    <div className="inspector-row">
      <span className="inspector-label">{label}</span>
      <span className="inspector-value">{value}</span>
    </div>
  )
}

export function SelectField({
  label,
  value,
  options,
  labels,
  editable,
  onCommit,
}: {
  label: string
  value: string
  options: readonly string[]
  labels?: readonly string[]
  editable: boolean
  onCommit: (value: string) => void
}) {
  return (
    <label className="inspector-row">
      <span className="inspector-label">{label}</span>
      <select value={value} disabled={!editable} onChange={(event) => onCommit(event.target.value)}>
        {options.map((option, index) => (
          <option key={option} value={option}>
            {labels?.[index] ?? option}
          </option>
        ))}
      </select>
    </label>
  )
}

export function CheckField({
  label,
  checked,
  editable,
  onCommit,
}: {
  label: string
  checked: boolean
  editable: boolean
  onCommit: (value: boolean) => void
}) {
  return (
    <label className="inspector-row">
      <span className="inspector-label">{label}</span>
      <input type="checkbox" checked={checked} disabled={!editable} onChange={(event) => onCommit(event.target.checked)} />
    </label>
  )
}

export function DateField({
  label,
  value,
  editable,
  onCommit,
}: {
  label: string
  value: string | null
  editable: boolean
  onCommit: (value: string | null) => void
}) {
  return (
    <label className="inspector-row">
      <span className="inspector-label">{label}</span>
      <input
        type="datetime-local"
        value={value === null ? '' : value.slice(0, 16)}
        readOnly={!editable}
        onChange={(event) => onCommit(event.target.value === '' ? null : event.target.value + ':00')}
      />
    </label>
  )
}
