import { describe, expect, it } from 'vitest'
import { windowRange } from './virtualize'

describe('windowRange', () => {
  it('windows from the top', () => {
    const window_ = windowRange(0, 400, 28, 1000, 5)
    expect(window_.first).toBe(0)
    expect(window_.last).toBe(Math.ceil(400 / 28) + 10)
    expect(window_.offsetY).toBe(0)
    expect(window_.totalHeight).toBe(28_000)
  })

  it('applies overscan above and below', () => {
    const window_ = windowRange(2800, 400, 28, 1000, 5)
    expect(window_.first).toBe(100 - 5)
    expect(window_.offsetY).toBe(95 * 28)
  })

  it('clamps at the end of the list', () => {
    const window_ = windowRange(27_600, 400, 28, 1000, 5) // scrolled to the very end
    expect(window_.last).toBe(1000)
    expect(window_.first).toBeLessThan(1000)
  })

  it('handles empty lists', () => {
    const window_ = windowRange(0, 400, 28, 0)
    expect(window_.first).toBe(0)
    expect(window_.last).toBe(0)
    expect(window_.totalHeight).toBe(0)
  })
})
