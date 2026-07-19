import { describe, expect, it } from 'vitest'
import { withGanttColumns, withResourcesColumns, withTableColumns } from './preferences'

describe('preferences', () => {
  it('merges a Gantt column selection without touching other scopes', () => {
    const next = withGanttColumns({ resources: ['name'] }, ['name', 'start'])
    expect(next).toEqual({ resources: ['name'], gantt: ['name', 'start'] })
  })

  it('merges a Resources column selection without touching other scopes', () => {
    const next = withResourcesColumns({ gantt: ['name'] }, ['name', 'rate'])
    expect(next).toEqual({ gantt: ['name'], resources: ['name', 'rate'] })
  })

  it('merges one Table subview without disturbing the others', () => {
    const before = { table: { entry: ['id', 'name'] } }
    const next = withTableColumns(before, 'evm', ['id', 'cpi'])
    expect(next).toEqual({ table: { entry: ['id', 'name'], evm: ['id', 'cpi'] } })
  })
})
