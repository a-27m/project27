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
