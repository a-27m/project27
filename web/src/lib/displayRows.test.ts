import { describe, expect, it } from 'vitest'
import { buildDisplayRows, displayIndexByUid } from './displayRows'

const tasks = [
  { uid: 1, spaceAfter: 0 },
  { uid: 2, spaceAfter: 2 },
  { uid: 3, spaceAfter: 0 },
]

describe('buildDisplayRows', () => {
  it('inserts a gap row per unit of spaceAfter, right after its task', () => {
    const rows = buildDisplayRows(tasks)
    expect(rows).toEqual([
      { kind: 'task', task: tasks[0] },
      { kind: 'task', task: tasks[1] },
      { kind: 'gap', afterUid: 2, index: 0 },
      { kind: 'gap', afterUid: 2, index: 1 },
      { kind: 'task', task: tasks[2] },
    ])
  })

  it('is a no-op when nothing has spacing', () => {
    const plain = [{ uid: 1, spaceAfter: 0 }]
    expect(buildDisplayRows(plain)).toEqual([{ kind: 'task', task: plain[0] }])
  })
})

describe('displayIndexByUid', () => {
  it('maps each uid to its slot in the gap-inclusive row list, skipping gaps', () => {
    const rows = buildDisplayRows(tasks)
    expect(displayIndexByUid(rows)).toEqual(
      new Map([
        [1, 0],
        [2, 1],
        [3, 4],
      ]),
    )
  })
})
