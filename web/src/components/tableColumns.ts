export const TABLES = ['entry', 'schedule', 'cost', 'work', 'tracking', 'variance', 'evm', 'summary'] as const

/** Mirrors Core's `TaskView.Tables` — what the server applies when no `fields=` override is sent. */
export const TABLE_DEFAULT_FIELDS: Record<string, readonly string[]> = {
  entry: ['id', 'name', 'duration', 'start', 'finish', 'predecessors', 'resourceNames'],
  schedule: ['id', 'name', 'start', 'finish', 'lateStart', 'lateFinish', 'freeSlack', 'totalSlack'],
  cost: ['id', 'name', 'fixedCost', 'cost', 'baselineCost', 'costVariance'],
  work: ['id', 'name', 'work', 'baselineWork', 'workVariance', 'percentComplete'],
  tracking: ['id', 'name', 'actualStart', 'actualFinish', 'percentComplete', 'remainingDuration'],
  variance: ['id', 'name', 'start', 'finish', 'baselineStart', 'baselineFinish', 'startVariance', 'finishVariance'],
  evm: ['id', 'name', 'bcws', 'bcwp', 'acwp', 'sv', 'cv', 'spi', 'cpi', 'bac', 'eac', 'vac'],
  summary: ['id', 'name', 'duration', 'start', 'finish', 'percentComplete', 'cost', 'work'],
}
