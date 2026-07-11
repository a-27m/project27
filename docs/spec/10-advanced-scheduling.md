# Phase 10 — Advanced scheduling

Resource leveling, task drivers (inspector), and resource sharing. Inactive
tasks shipped in phase 2. **Master/subprojects and cross-project links are an
extension point only** (product decision 2026-07-11): the seams below are
documented, nothing is implemented; the topic is revisited after phases
11/12/5.

## Resource leveling

A new leaf-task input, **leveling delay** (working minutes on the task
calendar), applied by the scheduler after dependencies and constraints have
produced the early start. Only the leveler writes it; `ClearLeveling()` resets
it everywhere.

`Project.Level()` (clears previous delays first, like MSP's "Level All"):

1. Recalculate; compute per-resource **daily demand** from the time-phased
   buckets (phase 9c) of active, unstarted, auto-scheduled leaf tasks.
2. Daily **capacity** = `MaxUnits ×` the working minutes of that day on the
   resource's calendar (falling back to the project calendar).
3. Take the earliest overallocated (resource, day); among the contributing
   tasks pick the victim: lowest `Priority` first, then largest total slack,
   then latest start, then highest row. Tasks with **priority 1000 are never
   leveled** (MSP semantics); neither are started, manual, milestone, or
   must-start-on/must-finish-on tasks.
4. Increase the victim's leveling delay so it starts on the next working
   moment after the conflicted day; recalculate; repeat.
5. Stop when no overallocation remains or nothing more can be delayed; the
   result reports the delays applied and any remaining overallocations.

Deviations: delay counts in working (not elapsed) time (#27); the victim
ordering is a fixed clean-room rule rather than MSP's configurable set (#28);
tasks with progress are skipped entirely rather than split-leveled (#29).

## Task drivers (inspector)

`TaskDrivers.Explain(task)` reproduces the forward-pass reasoning read-only and
returns the influences on the task's start with a **binding** flag (the ones
that actually set it): project anchor, each predecessor's imposed point,
constraint dates, actual-start/manual pins, leveling delay, and the governing
calendar. CLI: `p27 task drivers <ref>`.

## Resource sharing ("pools")

`Project.ImportResources(source)` copies resource definitions (type, alias
fields, calendars are **not** copied — the target's calendar of the same name
is linked when present, else none) with their rate tables; name clashes are
skipped and reported. CLI: `p27 resource import --from other.p27`. A live
shared pool (one file, many sharers) is the same cross-file concern as
subprojects — same extension point, revisited together (deviation #30).

## Extension point: subprojects & cross-project links

Reserved seams, no behavior:

- `DependencyDocument`/`TaskDocument` tolerate unknown JSON members (already
  true — deserialization ignores extras), so an `externalProject`/`externalUid`
  pair can be added in a later schema bump without breaking older files.
- The engine treats a future "external task" as a fixed-date island — exactly
  what a manual task already is; importers can materialize external
  predecessors as manual tasks until real linking exists.
- The server's snapshot store keys everything by project id; a master project
  is a client-side composition concern in this architecture.

## Persistence (schema v5)

`TaskDocument` += `LevelingDelayMinutes` (absent = 0). v1–v4 documents load.

## CLI & commands

```
p27 level run            # level all resources; reports delays + leftovers
p27 level clear          # remove every leveling delay
p27 task drivers <ref>   # what places the task where it is
p27 resource import --from <file.p27>
```

Command ops: `level`, `clearLeveling`. The field catalog gains
`levelingDelay`; the schedule projection carries `levelingDelayMinutes`.
