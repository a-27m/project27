import type { CostRateTableId } from '../api/types'

export const RATE_TABLE_IDS: readonly CostRateTableId[] = ['a', 'b', 'c', 'd', 'e']

export function rateTableLabel(table: CostRateTableId): string {
  return `Table ${table.toUpperCase()}`
}

/** Formats an entry's effective date for display; `null` is the base entry (always in force first). */
export function formatEffectiveFrom(from: string | null): string {
  return from === null ? 'Base (always effective)' : from.slice(0, 10)
}
