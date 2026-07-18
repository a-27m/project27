# Example projects

Nine `.p27` files that exercise Project27's feature set end to end. Seven are
small, single-feature demos; one is a 130-task realistic product-launch plan
that combines most of the engine's advanced behavior at once. All were built
non-interactively with the `p27` CLI (no hand-authored SQLite) — see
"Regenerating" below for the exact command sequences.

Open any of them with:

```sh
p27 -f examples/<dir>/<file>.p27 task list
p27 -f examples/<dir>/<file>.p27 view --table schedule
```

or point the server/web app at the file (`docker compose up --build`, then
import via the web UI, or `cp` it into a working directory the server serves
from).

## Small, single-feature demos

### 01 — Critical path & task drivers — [`website-relaunch.p27`](01-critical-path/website-relaunch.p27)
A 13-task website relaunch with FS/SS/FF/SF links and lags. A launch deadline
six weeks out is one day tighter than the network allows, so the whole
critical chain shows −1d total slack. Try:
```sh
p27 -f 01-critical-path/website-relaunch.p27 task list --critical
p27 -f 01-critical-path/website-relaunch.p27 task drivers 13   # "Go live" — why this date
```
Also includes [`website-relaunch.mspdi.xml`](01-critical-path/website-relaunch.mspdi.xml),
exported via `export mspdi` as an MS Project interop sample (round-trip with
`import mspdi`).

### 02 — Calendars & exceptions — [`data-center-migration.p27`](02-calendars-and-exceptions/data-center-migration.p27)
A 7-task cutover plan on a company calendar with recurring yearly holidays,
plus a `NightOps` calendar (built from the `night-shift` preset) for the live
cutover window, assigned per-task with `--ignore-resource-calendars` so the
engineers' day-shift calendars don't block it.
```sh
p27 -f 02-calendars-and-exceptions/data-center-migration.p27 calendar show NightOps
p27 -f 02-calendars-and-exceptions/data-center-migration.p27 task show 5   # Live cutover window
```

### 03 — Resource leveling — [`sprint-overallocated.p27`](03-resource-leveling/sprint-overallocated.p27) / [`sprint-leveled.p27`](03-resource-leveling/sprint-leveled.p27)
Five API tasks all want the same engineer at 100% units on overlapping days.
`sprint-overallocated.p27` is the raw plan (Dana double- and triple-booked
most days); `sprint-leveled.p27` is the same plan after
`level run --order priority --granularity day`, which serializes the work by
task priority.
```sh
p27 -f 03-resource-leveling/sprint-overallocated.p27 usage --assignments
p27 -f 03-resource-leveling/sprint-leveled.p27 usage --assignments
```
**Note:** task priorities in this file are deliberately distinct. p27's
leveler currently mishandles ties — see "Bugs found" below.

### 04 — Cost tracking & materials — [`trade-show-booth.p27`](04-cost-and-materials/trade-show-booth.p27)
A 7-task booth build mixing a work resource with an effective-dated rate
change, a material resource consumed per day of active work (plywood
sheets), a cost resource (permit fee), a fixed end-accrued delivery
surcharge, and a subcontractor billed on rate table B instead of the default
table A.
```sh
p27 -f 04-cost-and-materials/trade-show-booth.p27 view --table cost
```

### 05 — Baselines & earned value — [`mobile-app-v2.p27`](05-baselines-and-evm/mobile-app-v2.p27)
A 7-task mobile build baselined (slot 0) before work starts, then fast-
forwarded to a status date mid-project: Discovery and UX Design finished on
time, Auth is 60% done but running behind, Catalog started late. Full
BCWS/BCWP/ACWP/SPI/CPI/EAC output:
```sh
p27 -f 05-baselines-and-evm/mobile-app-v2.p27 task evm
p27 -f 05-baselines-and-evm/mobile-app-v2.p27 view --table variance
```

### 06 — Custom fields & indicators — [`vendor-onboarding.p27`](06-custom-fields-and-indicators/vendor-onboarding.p27)
A 6-task vendor-onboarding checklist with a `RiskScore` number field driven
entirely by a formula (`IIf([totalSlack] <= 0d, 100, ...)`) with red/amber/
green indicator thresholds, plus a manually-set `VendorTier` text field for
grouping. Security review carries a deadline that pushes its own slack
negative, lighting the risk flag red live off the schedule.
```sh
p27 -f 06-custom-fields-and-indicators/vendor-onboarding.p27 view --table entry --fields riskscore,vendortier
```

### 07 — Recurring tasks & splits — [`consulting-engagement.p27`](07-recurring-and-splits/consulting-engagement.p27)
A consulting engagement with a biweekly recurring status call
(`task add-recurring`, one summary + 16 occurrences) and a mid-flight
analysis task split around a one-week interruption (`task split --at --gap`).
```sh
p27 -f 07-recurring-and-splits/consulting-engagement.p27 task list
```

## The big one

### 10 — Apex Platform Launch — [`apex-platform-launch.p27`](10-apex-platform-launch/apex-platform-launch.p27)
A ~9-month hardware + firmware + cloud + mobile product launch: **131 tasks**
(121 leaf, 7 milestones) across 9 phases (Discovery, Hardware Engineering,
Firmware, Application Software, Manufacturing & Supply Chain, Certification,
QA, Marketing, Launch), run by 14 work resources, 1 material resource (PCB
prototype runs), and 2 cost resources (cert lab fees, mold tooling).

It layers together nearly every feature in the small demos plus a few that
only make sense at scale:

- **Calendars**: a company calendar with recurring holidays, plus a 24h
  `TestLab` calendar for the burn-in test, applied with
  `--ignore-resource-calendars`.
- **Constraints & deadlines**: the FCC/CE submission has a start-no-earlier-
  than constraint (external lab booking); the public launch day carries a
  hard deadline against a trade-show date.
- **Effort-driven scheduling**: the mobile app core UX task is
  `fixed-work`/effort-driven.
- **Cost & materials**: time-varying rates, per-day material consumption,
  flat cost-resource expenses, a rate-table split for outside cert labs.
- **A supply-chain split**: long-lead component procurement is interrupted
  by a customs hold (`task split`).
- **A recurring program cadence**: weekly status calls from kickoff through
  the launch gate.
- **Custom fields**: a live `RiskScore` indicator (same formula pattern as
  demo 06) plus a `WorkPackage` tracking code.
- **Resource leveling**: run once across the full resource-loaded plan
  before baselining.
- **Baseline + EVM**: baselined at kickoff, then progressed to a status date
  five weeks in, with realistic mixed-pace progress (some work ahead, some
  behind) driving genuine SPI/CPI/EAC figures.
- **Critical path**: the true critical chain runs through pilot build →
  mass production → launch readiness → launch day (0d slack); certification
  and QA sign-off sit on separate, non-critical branches with real float.

```sh
p27 -f 10-apex-platform-launch/apex-platform-launch.p27 task list --critical
p27 -f 10-apex-platform-launch/apex-platform-launch.p27 task drivers 130   # "Public launch day"
p27 -f 10-apex-platform-launch/apex-platform-launch.p27 task evm
p27 -f 10-apex-platform-launch/apex-platform-launch.p27 usage -g week --cost
```

[`status-report.html`](10-apex-platform-launch/status-report.html) is a
pre-generated self-contained overview report (`report overview`) — open it
directly in a browser.

## Bugs found while building these

Two engine issues turned up during scripting, neither worked around silently
— noted here so they're not mistaken for intentional example behavior:

1. **`level run` breaks on tied task priority.** Three or more tasks with
   the *same* priority competing for one resource produce delays of
   hundreds of days (one repro pushed a 3-day task out by 347 days) and the
   run still reports the resource overallocated afterward, regardless of
   `--granularity`. Two tasks, or 3+ tasks with distinct priorities, level
   correctly (confirmed by direct repro). The `sprint-overallocated.p27`
   demo above uses distinct priorities specifically to avoid this.
2. **`export mspdi` throws on any project with a `night-shift`- or
   `24h`-preset calendar** (`Ticks must be between 0 and TimeOnly.MaxValue.Ticks`),
   even with no exceptions or assignments on that calendar — reproduced with
   just `calendar add X --preset night-shift` on an otherwise-empty project.
   This is why `apex-platform-launch.p27` (which needs a 24h `TestLab`
   calendar) doesn't ship an `.mspdi.xml` export alongside it; the
   `website-relaunch.p27` demo does, since it has no custom calendars.

## Regenerating

These files were built with small Python scripts driving the `p27` CLI
(`init` → `task add` → `link add` → `resource add` → `assign add` → ...,
capturing each created task's `uid` from `--json` output to reference it in
later commands). No `.p27` file here was hand-edited. If you want to
regenerate or extend one of these, that's the pattern to follow: script the
CLI, don't touch the SQLite directly.
