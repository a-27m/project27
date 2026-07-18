import { useRef, useState } from 'react'

const STOPS = [0, 25, 50, 75, 100] as const

interface Props {
  value: number
  editable: boolean
  onCommit: (percent: number) => void
  ariaLabel?: string
}

/** Segmented % complete control: click a stop to jump, or drag the fill/knob for an in-between value. */
export function SegmentedPercent({ value, editable, onCommit, ariaLabel = '% complete' }: Props) {
  const [dragValue, setDragValue] = useState<number | null>(null)
  const trackRef = useRef<HTMLDivElement>(null)
  const display = dragValue ?? value

  function percentAtClientX(clientX: number): number {
    const rect = trackRef.current!.getBoundingClientRect()
    const ratio = Math.min(1, Math.max(0, (clientX - rect.left) / rect.width))
    return Math.round(ratio * 100)
  }

  function beginDrag(event: React.PointerEvent) {
    if (!editable) return
    event.currentTarget.setPointerCapture(event.pointerId)
    setDragValue(percentAtClientX(event.clientX))
  }

  function moveDrag(event: React.PointerEvent) {
    if (!editable || dragValue === null) return
    setDragValue(percentAtClientX(event.clientX))
  }

  function endDrag(event: React.PointerEvent) {
    if (!editable || dragValue === null) return
    event.currentTarget.releasePointerCapture(event.pointerId)
    onCommit(percentAtClientX(event.clientX))
    setDragValue(null)
  }

  return (
    <div
      className="pct-slider"
      ref={trackRef}
      role="slider"
      aria-label={ariaLabel}
      aria-valuemin={0}
      aria-valuemax={100}
      aria-valuenow={display}
      title="% complete — click a stop or drag toward it"
      onPointerDown={beginDrag}
      onPointerMove={moveDrag}
      onPointerUp={endDrag}
    >
      <div className="pct-slider-fill" style={{ width: `${display}%` }} />
      <span className="pct-slider-knob" style={{ left: `${display}%` }} />
      {STOPS.map((stop) => (
        <button
          key={stop}
          type="button"
          className="pct-slider-stop"
          style={{
            left: `${stop}%`,
            color: stop <= display ? 'var(--accent)' : 'var(--muted)',
            fontWeight: stop === display ? 700 : 400,
          }}
          disabled={!editable}
          title={`${stop}%`}
          onClick={(event) => {
            event.stopPropagation()
            onCommit(stop)
          }}
        >
          {stop}
        </button>
      ))}
    </div>
  )
}
