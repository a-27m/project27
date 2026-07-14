import type { CSSProperties } from 'react'
import { ICON_DATA } from './icon-data'

interface Props {
  name: string
  size?: number
  className?: string
  style?: CSSProperties
}

/** Carbon icon, painted with currentColor. Adapted from the DS's Icon.jsx —
 *  same materialized glyph data, rendered via dangerouslySetInnerHTML since the
 *  body strings are emitter-controlled <path>/<circle>/<rect> markup only. */
export function Icon({ name, size = 16, className, style }: Props) {
  const entry = ICON_DATA[name]
  if (entry === undefined) return null
  return (
    <svg
      width={size}
      height={size}
      viewBox={entry.viewBox}
      fill="none"
      className={className}
      style={style}
      aria-hidden="true"
      dangerouslySetInnerHTML={{ __html: entry.body }}
    />
  )
}
