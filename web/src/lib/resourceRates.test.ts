import { describe, expect, it } from 'vitest'
import { formatEffectiveFrom, RATE_TABLE_IDS, rateTableLabel } from './resourceRates'

describe('resourceRates', () => {
  it('lists all five rate tables in order', () => {
    expect(RATE_TABLE_IDS).toEqual(['a', 'b', 'c', 'd', 'e'])
  })

  it('labels a table by its letter', () => {
    expect(rateTableLabel('a')).toBe('Table A')
    expect(rateTableLabel('e')).toBe('Table E')
  })

  it('formats the base entry distinctly from a dated entry', () => {
    expect(formatEffectiveFrom(null)).toBe('Base (always effective)')
    expect(formatEffectiveFrom('2026-01-15T00:00:00Z')).toBe('2026-01-15')
  })
})
