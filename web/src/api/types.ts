// Wire types of the Project27 server API (docs/spec/06-server.md, 07-web-foundation.md).

export type Role = 'reader' | 'editor' | 'owner'

export interface LockInfo {
  userId: string
  displayName: string
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

export interface VersionInfo {
  imageTag: string
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
  levelingDelayMinutes: number
  priority: number
  /** Blank display rows shown after this task; cosmetic only, never scheduled or linked. */
  spaceAfter: number
  type: 'fixedUnits' | 'fixedDuration' | 'fixedWork'
  effortDriven: boolean
  ignoresResourceCalendars: boolean
  fixedCost: number
  fixedCostAccrual: 'start' | 'prorated' | 'end'
  manualStart: string | null
  manualFinish: string | null
  calendar: string | null
  assignments: ScheduleAssignment[]
  customValues: Record<string, unknown> | null
}

export interface ScheduleAssignment {
  resource: string
  resourceType: 'work' | 'material' | 'cost'
  units: number
  workMinutes: number
  contour: string
  delayMinutes: number
  rateTable: 'a' | 'b' | 'c' | 'd' | 'e'
  cost: number
  costInput: number
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
  customFields: CustomFieldSummary[]
  stats: ProjectStats
}

export interface DateStat {
  current: string | null
  baseline: string | null
  actual: string | null
  varianceMinutes: number | null
}

export interface AmountStat {
  current: number
  baseline: number | null
  actual: number | null
  remaining: number | null
}

export interface ProjectStats {
  start: DateStat
  finish: DateStat
  duration: AmountStat
  work: AmountStat
  cost: AmountStat
  percentCompleteByDuration: number
  percentCompleteByWork: number
}

export interface CustomFieldSummary {
  id: string
  alias: string | null
  kind: string
  hasFormula: boolean
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
  inverse: Command[] | null
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
      spaceAfter?: number
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
  | { op: 'setProject'; name?: string; start?: string; statusDate?: string; clearStatusDate?: boolean }
  | { op: 'assign'; uid: number; resource: string; units?: number; work?: string; cost?: number }
  | { op: 'setAssignment'; uid: number; resource: string; units?: number; work?: string; contour?: string; delay?: string; rateTable?: string; cost?: number }
  | { op: 'unassign'; uid: number; resource: string }
  | { op: 'setBaseline'; slot?: number }
  | { op: 'clearBaseline'; slot?: number }
  | { op: 'level' }
  | { op: 'clearLeveling' }
  | { op: 'reschedule'; after?: string }
  | { op: string; [key: string]: unknown }

export interface ProjectEvent {
  kind: 'checkout' | 'checkin' | 'lock-released'
  data: unknown
}

export interface ViewField {
  key: string
  caption: string
  kind: string
}

export interface ViewRow {
  uid: number
  id: number
  values: Record<string, unknown>
}

export interface ViewGroup {
  heading: string | null
  rows: ViewRow[]
}

export interface ViewResult {
  fields: ViewField[]
  groups: ViewGroup[]
}

export interface TaskDriver {
  kind: string
  description: string
  binding: boolean
  date: string | null
  predecessorUid: number | null
}

export interface SnapshotInfo {
  version: number
  savedBy: string
  savedByName: string
  savedAt: string
  label: string | null
}
