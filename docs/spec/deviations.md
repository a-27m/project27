# Deviations from MS Project behavior

Per decision D7 (clean-room engine) we document every known behavioral difference
from MS Project desktop. Entries are added as phases land.

| # | Area | Our behavior | MS Project | Rationale |
|---|------|--------------|------------|-----------|
| 1 | Calendar options | Default week start: Monday (ISO 8601) | Sunday (US locale default) | Saner international default; MSPDI import applies the file's setting anyway |
| 2 | Recurring exceptions | Weekly recurrence "every N weeks" anchors weeks on Monday regardless of project week-start | Anchored to locale week | Determinism independent of display settings |
| 3 | Recurring exceptions | Monthly/yearly "day 31" clamps to the month's last day | Skips/clamps inconsistently by pattern | Predictable; never silently drops an occurrence |
| 4 | Calendar resolution | A work week that covers a date but leaves the weekday undefined falls through to default-week resolution | Believed equivalent ("use default times" option) | Documented because MSP behavior here is under-specified |
