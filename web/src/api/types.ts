// Wire types of the Project27 server API (docs/spec/06-server.md, 07-web-foundation.md).

export type Role = 'reader' | 'editor' | 'owner'

export interface LockInfo {
  userId: string
  acquiredAt: string
  refreshedAt: string
  stale: boolean
}

export interface ProjectInfo {
  id: string
  name: string
  version: number
  createdBy: string
  createdAt: string
  role: Role
  lock: LockInfo | null
}

export interface Me {
  id: string
  name: string
}

export type DependencyType = 'finishToStart' | 'startToStart' | 'finishToFinish' | 'startToFinish'
export type LagKind = 'working' | 'elapsed' | 'percent'
export type TaskMode = 'auto' | 'manual'
export type ConstraintType =
  | 'asSoonAsPossible'
  | 'asLateAsPossible'
  | 'startNoEarlierThan'
  | 'startNoLaterThan'
  | 'finishNoEarlierThan'
  | 'finishNoLaterThan'
  | 'mustStartOn'
  | 'mustFinishOn'

export interface SchedulePredecessor {
  predecessorUid: number
  type: DependencyType
  lagKind: LagKind
  lagValue: number
}

export interface ScheduleSegment {
  start: string
  finish: string
}

export interface ScheduleTask {
  uid: number
  row: number
  name: string
  outlineLevel: number
  wbs: string
  summary: boolean
  milestone: boolean
  recurring: boolean
  critical: boolean
  active: boolean
  mode: TaskMode
  durationMinutes: number
  estimated: boolean
  start: string | null
  finish: string | null
  totalSlackMinutes: number | null
  freeSlackMinutes: number | null
  constraint: ConstraintType
  constraintDate: string | null
  deadline: string | null
  workMinutes: number
  cost: number
  segments: ScheduleSegment[]
  predecessors: SchedulePredecessor[]
  percentComplete: number
  actualStart: string | null
  actualFinish: string | null
  baselineStart: string | null
  baselineFinish: string | null
  baselineCost: number | null
}

export interface ScheduleProject {
  id: string
  name: string
  start: string
  finish: string | null
  scheduleFrom: 'projectStart' | 'projectFinish'
  minutesPerDay: number
  calendar: string
  totalWorkMinutes: number
  totalCost: number
  statusDate: string | null
  calendars: string[]
  resources: ResourceSummary[]
}

export interface ResourceSummary {
  uid: number
  name: string
  type: 'work' | 'material' | 'cost'
  maxUnits: number
  rate: string
}

export interface Schedule {
  version: number
  project: ScheduleProject
  tasks: ScheduleTask[]
}

export interface UsageBucket {
  date: string
  workMinutes: number
  cost: number
}

export interface UsageRow {
  uid: number
  row: number
  name: string
  outlineLevel: number
  summary: boolean
  buckets: UsageBucket[]
  totalWorkMinutes: number
  totalCost: number
}

export interface Usage {
  version: number
  granularity: 'day' | 'week'
  weekStartsOn: string
  rows: UsageRow[]
}

export interface CommandsResponse {
  version: number
  createdUids: (number | null)[]
  schedule: Schedule
}

export interface Checkout {
  version: number
  lock: LockInfo
}

// Commands (op-discriminated; docs/spec/07-web-foundation.md).

export interface CommandLag {
  kind: LagKind
  value: number
}

export type Command =
  | { op: 'addTask'; name: string; duration?: string; parentUid?: number; at?: number; milestone?: boolean }
  | {
      op: 'setTask'
      uid: number
      name?: string
      duration?: string
      mode?: TaskMode
      active?: boolean
      milestone?: boolean
      priority?: number
      deadline?: string
      clearDeadline?: boolean
      constraint?: ConstraintType
      constraintDate?: string
    }
  | { op: 'removeTask'; uid: number }
  | { op: 'moveTask'; uid: number; parentUid?: number; at: number }
  | { op: 'indentTask'; uid: number }
  | { op: 'outdentTask'; uid: number }
  | { op: 'link'; predecessorUid: number; successorUid: number; type?: DependencyType; lag?: CommandLag }
  | { op: 'setLink'; predecessorUid: number; successorUid: number; type?: DependencyType; lag?: CommandLag }
  | { op: 'unlink'; predecessorUid: number; successorUid: number }
  | { op: 'setProject'; name?: string; start?: string }

export interface ProjectEvent {
  kind: 'checkout' | 'checkin' | 'lock-released'
  data: unknown
}
