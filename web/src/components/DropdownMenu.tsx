import { useRef, useState, type ReactNode } from 'react'
import { useOutsideClose } from '../lib/useOutsideClose'
import { Icon } from './icons/Icon'

export interface MenuItem {
  label: string
  onClick: () => void
  danger?: boolean
}

export interface MenuGroup {
  heading?: string
  items: MenuItem[]
}

interface Props {
  /** Render-prop so callers can style their own trigger (icon-only, labeled, avatar…). */
  trigger: (state: { open: boolean; toggle: () => void }) => ReactNode
  groups: MenuGroup[]
  ariaLabel: string
  align?: 'left' | 'right'
}

/** Carbon-style popover menu — adapted from the DS's OverflowMenu.jsx (click-outside
 *  close, role="menu"), generalized to a custom trigger and optional item groups. */
export function DropdownMenu({ trigger, groups, ariaLabel, align = 'left' }: Props) {
  const [open, setOpen] = useState(false)
  const ref = useRef<HTMLDivElement>(null)
  useOutsideClose(ref, open, () => setOpen(false))

  return (
    <div className="dropdown" ref={ref}>
      {trigger({ open, toggle: () => setOpen((o) => !o) })}
      {open && (
        <ul className={`dropdown-panel ${align === 'right' ? 'align-right' : 'align-left'}`} role="menu" aria-label={ariaLabel}>
          {groups.map((group, gi) => (
            <li key={gi} role="none">
              {group.heading !== undefined && <div className="dropdown-heading">{group.heading}</div>}
              <ul role="none" className="dropdown-group">
                {group.items.map((item, ii) => (
                  <li key={ii} role="none">
                    <button
                      type="button"
                      role="menuitem"
                      className={`dropdown-item${item.danger === true ? ' danger' : ''}`}
                      onClick={() => {
                        setOpen(false)
                        item.onClick()
                      }}
                    >
                      {item.label}
                    </button>
                  </li>
                ))}
              </ul>
            </li>
          ))}
        </ul>
      )}
    </div>
  )
}

/** A text trigger button styled like the app's other action-bar controls, with a caret. */
export function DropdownTrigger({ label, open, onClick, className }: { label: string; open: boolean; onClick: () => void; className?: string }) {
  return (
    <button type="button" className={`icon-btn dropdown-trigger${className !== undefined ? ' ' + className : ''}`} aria-haspopup="menu" aria-expanded={open} onClick={onClick}>
      {label}
      <Icon name="CaretDown" size={12} />
    </button>
  )
}
