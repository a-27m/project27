# Deviations from MS Project behavior

Per decision D7 (clean-room engine) we document every known behavioral difference
from MS Project desktop. Entries are added as phases land.

| # | Area | Our behavior | MS Project | Rationale |
|---|------|--------------|------------|-----------|
| 1 | Calendar options | Default week start: Monday (ISO 8601) | Sunday (US locale default) | Saner international default; MSPDI import applies the file's setting anyway |
| 2 | Recurring exceptions | Weekly recurrence "every N weeks" anchors weeks on Monday regardless of project week-start | Anchored to locale week | Determinism independent of display settings |
| 3 | Recurring exceptions | Monthly/yearly "day 31" clamps to the month's last day | Skips/clamps inconsistently by pattern | Predictable; never silently drops an occurrence |
| 4 | Calendar resolution | A work week that covers a date but leaves the weekday undefined falls through to default-week resolution | Believed equivalent ("use default times" option) | Documented because MSP behavior here is under-specified |
| 5 | Dependencies | Lag is evaluated on the successor's calendar (elapsed lag on clock time) | Under-documented; believed similar | Deterministic, single rule for all lag kinds |
| 6 | Summary links | FF/SF links into a summary bind **every** leaf's finish; FS/SS bind every leaf's start | Inherited-link behavior is under-specified for FF/SF | Uniform "inherited by all subtasks" rule |
| 7 | Manual tasks | Manual summary tasks are not supported — summaries always roll up | MSP allows manually scheduled summaries | Rollup consistency; revisit if demanded |
| 8 | Scheduling display | Dates may land on equivalent working-time boundaries (e.g. finish "Wed 08:00" ≡ "Tue 17:00") when driven by start-of-day bounds | MSP normalizes to interval ends | Zero-width working-time equivalence; cosmetic |
| 9 | Recurring tasks | Require an end date or occurrence count; capped at 999 occurrences | Same cap; end bound also required | Parity, made explicit |
| 10 | Fields (CLI/JSON) | `outlineLevel` is 0-based (top level = 0) | OutlineLevel is 1-based | Engine convention; MSPDI interop will emit/consume 1-based |
| 11 | Resources | Names are unique per project (case-insensitive) | Duplicate names allowed | Unambiguous CLI/JSON addressing by name |
| 12 | Assignments | Only leaf tasks can be assigned | Summary assignments allowed (discouraged) | Rollup consistency; revisit if demanded |
| 13 | Material resources | Assignment units are a fixed quantity | Also variable per-time consumption ("10/day") | Time-phased consumption needs usage views (phase 9) |
| 14 | Work contours | Clean-room decile averages; scheduling uses average utilization only (flat 1.0, back/front-loaded 0.60, double-peak/bell 0.50, early/late-peak 0.45, turtle 0.70) | Per-decile time-phased distribution | Distribution becomes observable with usage views (phase 9) |
| 15 | Rates | Yearly rates convert at 52 × MinutesPerWeek | Believed equivalent; under-documented | Deterministic, documented rule |
| 16 | Assignments | Split and manual tasks pin assignment dates to task dates (resource calendars not applied) | Resource calendars also shape split-task assignments | Complexity deferred; splits + assignments is a rare combination |
| 17 | Costs | The rate band in force at the assignment start prices the whole assignment | Prorates across bands per time-phased bucket | Needs time-phased work (phase 8); flagged for revisit |
| 18 | Scheduling | A restrictive resource calendar extends the task finish but the entered duration field is not rewritten | Duration is recalculated from the assignment span | Inputs stay inputs; the bar and slack reflect the real span |
| 19 | EVM | BCWS prorates baseline cost linearly over the baseline span's working time | Time-phased by cost accrual | Time-phased data arrives with usage views; flagged for revisit |
| 20 | Actuals | Actual work and cost derive from percent complete | Separately trackable actual work/cost per assignment | Explicit actuals land with usage views (phase 9) |
| 21 | Interim plans | Subsumed by full baseline slots 1–10 plus server snapshot history | Interim plans store start/finish only | Strictly more information retained |
| 22 | Tracking | Summary percent complete is read-only (rolls up) | Editing a summary % distributes to children | Distribution rules are opaque; explicit per-leaf updates are safer |
| 23 | Rescheduling | Already-split in-progress tasks are left alone by reschedule-uncompleted-work | Moves remaining segments | Split-of-split complexity deferred until demanded |
| 24 | Tables | Built-in tables (entry, schedule, cost, work, tracking, variance, evm, summary) are clean-room field selections | Table definitions differ in detail | Selections chosen for coherence; users can pass --fields |
| 25 | Views | Sorting or grouping flattens the view to leaf tasks | Optional "keep outline structure" reorders within the hierarchy | Deterministic and simple; hierarchy-preserving sort deferred |
| 26 | Custom fields | Formula language is a clean-room subset: [field] refs, arithmetic, comparisons, and/or/not, IIf/Abs/Min/Max/Round, Now/StatusDate, duration literals | VBA-flavored expression set with dozens of functions | Function set grows on demand; duration literals are an extension |
| 27 | Leveling | Leveling delay counts in working time on the task calendar | Elapsed-duration ("edays") delay | Consistent with every other delay in the engine |
| 28 | Leveling | Fixed victim order: priority, already-delayed, slack, latest start, row; whole-day steps | Configurable orders, minute-level and split-based leveling | Deterministic and predictable; refinements on demand |
| 29 | Leveling | Tasks with any progress are never leveled | Can level remaining work | Split-leveling of in-progress work deferred with usage editing |
| 30 | Resource pools | `resource import` copies definitions; no live shared pool | Linked pool files with sharer synchronization | Cross-file linking is the subprojects extension point (spec 10) |
