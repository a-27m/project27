import { describe, expect, it } from 'vitest'
import { RESOURCE_COLUMNS } from './resourceColumns'

describe('resourceColumns', () => {
  it('includes all standard resource columns', () => {
    const keys = RESOURCE_COLUMNS.map((c) => c.key)
    expect(keys).toContain('name')
    expect(keys).toContain('type')
    expect(keys).toContain('maxUnits')
    expect(keys).toContain('rate')
  })

  it('includes metadata columns for resources', () => {
    const keys = RESOURCE_COLUMNS.map((c) => c.key)
    expect(keys).toContain('initials')
    expect(keys).toContain('group')
    expect(keys).toContain('calendar')
    expect(keys).toContain('materialLabel')
    expect(keys).toContain('accrual')
  })

  it('has labels for all columns', () => {
    RESOURCE_COLUMNS.forEach((col) => {
      expect(col.label).toBeTruthy()
      expect(col.label.length).toBeGreaterThan(0)
    })
  })
})
