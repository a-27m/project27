# Spec 01 — Time, durations, calendars

Scope: Phase 1. Namespace `Project27.Core.Time`.

## Model

- **`TimeSettings`** — project-level conversion factors: minutes/day (480), minutes/week
  (2400), days/month (20), week start (Monday), default start/end times (08:00/17:00)
  applied when users enter dates without times.
- **`Duration`** — value + unit + estimated flag (`3d?`). Units: minutes/hours/days/
  weeks/months and elapsed variants (`ed`, `ew`, …). Working units convert to minutes
  through `TimeSettings`; elapsed units are clock time (day=24 h, week=168 h,
  month=30 × 24 h). Compact parse/format grammar: `<decimal> <unit>[?]`, e.g. `2.5w`,
  `4 edays`, `3d?`. Invariant culture.
- **`WorkCalendar`** — named calendar, optionally derived from a base calendar:
  - *default week*: per-weekday `DaySchedule?` — `null` means *inherit*;
    `DaySchedule.NonWorking` is an explicit day off.
  - *work weeks*: alternate weekly patterns scoped to a date range.
  - *exceptions*: date span or recurrence (daily/weekly/monthly-by-day/
    monthly-by-weekday/yearly), ending by date or occurrence count; schedule is
    non-working or specific hours.
  - Built-ins: Standard (Mon–Fri 8–12, 13–17), 24 Hours, Night Shift.
- **`DaySchedule`** — ordered, non-overlapping minute-granular intervals within one
  day (`0..1440`); may touch but not overlap.

## Day resolution order

For a date, walking the base-calendar chain most-derived first:

1. exceptions (first match wins, chain order),
2. work weeks covering the date **if** they define that weekday,
3. default week, per-day falling through `null` (inherit) down the chain,
4. otherwise non-working.

Consequence: a base calendar's exception (company holiday) shows through derived
resource calendars unless the derived calendar has its own exception that date.

## Working-time arithmetic (on `WorkCalendar`)

- `IsWorkingTime(t)` — inside a working interval, start-inclusive, end-exclusive.
- `NextWorkingTime(t)` / `PreviousWorkingTime(t)` — earliest ≥ t / latest ≤ t working
  instant; `Previous` treats interval *ends* as valid (a task can finish at 17:00).
- `AddWork(t, minutes)` — signed; negative walks backward. Result may land exactly on
  an interval boundary (end for forward, start for backward).
- `WorkBetween(a, b)` — signed working minutes.

All arithmetic is time-zone-naive (`DateTimeKind.Unspecified`), same as MS Project.
A scan guard throws if no working time is found within ~10 years (dead calendar);
`WorkBetween` over a bounded dead range returns 0 instead.

## Known deviations from MS Project

Recorded in `deviations.md`: week-start default, recurrence week anchoring,
month-day clamping.
