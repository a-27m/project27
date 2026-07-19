const STOPS = [0, 25, 50, 75, 100] as const

interface Props {
  value: number
  editable: boolean
  onCommit: (percent: number) => void
  ariaLabel?: string
}

/** Discrete % complete selector: 5 fixed segments, highlighted only on an exact match — not a progress bar. */
export function SegmentedPercent({ value, editable, onCommit, ariaLabel = '% complete' }: Props) {
  return (
    <div className="pct-segments" role="group" aria-label={ariaLabel}>
      {STOPS.map((stop) => (
        <button
          key={stop}
          type="button"
          className="pct-segment"
          aria-pressed={value === stop}
          data-active={value === stop || undefined}
          disabled={!editable}
          title={`Set ${stop}% complete`}
          onClick={(event) => {
            event.stopPropagation()
            onCommit(stop)
          }}
        >
          {stop === 100 ? '✓' : (
            <>
              {stop}
              <sup>%</sup>
            </>
          )}
        </button>
      ))}
    </div>
  )
}
