# Spec 02 — Tasks and CPM scheduling

Scope: Phase 2. Namespaces `Project27.Core` (domain), `Project27.Core.Scheduling` (engine).

## Task model

- **Outline**: tasks form a tree under a hidden root; summaries are tasks with
  children. Row numbers (MSP "ID") are pre-order positions; outline numbers are
  hierarchical (`1.2.3`); WBS defaults to outline number, custom override allowed.
- **Fields**: name, duration (with estimated flag; new tasks default `1d?`), mode
  (auto/manual), milestone (duration 0 or explicit flag), constraint + date, deadline,
  task calendar, priority 0–1000 (500), active flag, unique id (stable, MSPDI UID).
- **Summary tasks** are always rolled up from children (manual summaries not
  supported — deviation). Duration/start/finish on a summary are computed.
- **Split tasks**: a leaf may carry split parts (work part + gap, both working time on
  the task's calendar); scheduled segments are exposed. Editing duration clears splits.
- **Recurring tasks**: generated summary + occurrence children, each pinned with a
  SNET constraint, reusing the `Recurrence` patterns from spec 01. Requires an end
  date or occurrence count; capped at 999 occurrences.

## Dependencies

FS / SS / FF / SF with lag: working duration (signed, leads negative), elapsed
duration, or percentage of the predecessor's duration. Lag is evaluated on the
**successor's** calendar (deviation note). Links between a task and its own
ancestor/descendant are rejected; cycles are rejected at link time over the
leaf-expanded graph. Links to/from summaries are inherited by all leaf descendants
(FS/SS constrain leaf starts, FF/SF constrain leaf finishes — deviation note).

## Constraints

ASAP, ALAP, SNET, SNLT, FNET, FNLT, MSO, MFO + deadline. Semantics:

| Kind | Forward pass | Backward pass |
|------|--------------|---------------|
| SNET/FNET | raises early start/finish bound | — |
| SNLT/FNLT | — | lowers late start/finish bound |
| MSO/MFO | pins early date | pins late date (can produce negative slack) |
| ALAP | — | task takes its late dates as scheduled dates |
| Deadline | — | lowers late finish (slack/critical only, never moves dates) |

## Scheduling algorithm

Full recalculation (`Project.Recalculate()`), deterministic:

1. **Forward pass** — monotone worklist over the task graph (leaves, summaries as
   min/max rollups, manual tasks as fixed islands, inactive tasks scheduled but
   contributing nothing). Early start = max of project start, dependency bounds
   (own + inherited from ancestors), SNET/FNET/MSO/MFO. Dates snap to working time
   of the task calendar (else project calendar).
2. **Project finish** = max early finish over active tasks.
3. **Backward pass** — mirror, from project finish (schedule-from-finish projects
   anchor here and derive the start), computing late dates.
4. **Final dates** — ALAP tasks take late dates; others early dates. Split segments
   are placed by walking parts/gaps with calendar arithmetic.
5. **Slack** — total slack = min(work(Start→LS), work(Finish→LF)), signed; free
   slack = the least slip that would move any successor (or the project finish when
   no successors). Critical = total slack ≤ threshold (default 0).

Convergence is guaranteed (all updates move dates monotonically); a step cap turns
any internal error into an exception rather than a hang.

## Out of scope until later phases

Assignment-driven scheduling (task types × effort-driven interact with resources,
Phase 4); leveling (Phase 10); progress-based rescheduling (Phase 8).
