# Project27 user guide

A practical tour. Every workflow below works in the CLI; the web app covers the
same engine through the server (parity matrix: `docs/spec/12-polish.md`).
Command reference: `p27 --help` and `p27 <verb> --help`.

## 1. Plan a project (local file)

```sh
p27 init Alpha --start 2026-01-05        # creates alpha.p27
p27 task add "Design" -d 2d
p27 task add "Build"  -d 3d
p27 task add "Ship" --milestone
p27 link add 1 2                         # finish-to-start
p27 link add 2 3
p27 task list                            # dates, predecessors, critical path
```

Outline with `task indent/outdent/move`; interruptions with `task split`;
recurring work with `task add-recurring "Standup" -d 30m --recur weekly:mon,fri
--from 2026-01-05 --times 20`. Constraints and deadlines:
`task set 2 --constraint snet --constraint-date 2026-01-12 --deadline 2026-01-30`.

## 2. Calendars

```sh
p27 calendar add Ops --base Standard
p27 calendar set-day Ops sat 08:00-12:00
p27 calendar add-exception Standard "Christmas" --from 2026-12-25 --recur yearly-date:12-25 --times 5
p27 task set 2 --calendar Ops
```

## 3. Resources, assignments, costs

```sh
p27 resource add Dev --rate 50/h --max-units 200%
p27 resource add Cement --type material --rate 12.5 --material-label t
p27 resource add Travel --type cost
p27 assign add 2 Dev                     # work = duration × units
p27 assign add 2 Cement --units 10
p27 assign add 2 Travel --cost 300
p27 task show 2                          # work, cost, assignments
```

Task types control what recalculates on edits (`task set 2 --type fixed-work`),
effort-driven tasks keep total work when staffing changes
(`--effort-driven true`). Rates can change over time
(`resource set-rate Dev --from 2026-06-01 --rate 60/h`) and vary per assignment
(`assign set 2 Dev --table B`).

## 4. Track progress & earned value

```sh
p27 baseline set                         # freeze the plan (slots 0-10)
p27 project set --status-date 2026-01-12
p27 task set 2 --percent-complete 40
p27 task set 1 --actual-start 2026-01-05 --actual-finish 2026-01-07
p27 schedule reschedule                  # push uncompleted work past the status date
p27 task evm                             # BCWS/BCWP/ACWP, SPI/CPI, EAC…
```

## 5. Views, fields, reports

```sh
p27 view --table variance                # entry|schedule|cost|work|tracking|variance|evm|summary
p27 view --fields id,name,cost --filter "critical = true and cost > 1000" --sort "cost desc"
p27 field list                           # every field key
p27 customfield define number1 --alias Risk --formula "IIf([totalSlack] < 1d, 100, 0)" \
    --indicator "when >= 100 then red-flag"
p27 usage -g week --assignments          # time-phased work grid
p27 report overview -o status.html       # self-contained HTML; print to PDF from the browser
```

## 6. Resource leveling

```sh
p27 level run                            # delays lower-priority tasks out of overallocations
p27 task drivers 4                       # why is this task scheduled where it is?
p27 level clear
```

Tasks with priority 1000 are never moved; started work is left alone.

## 7. Team mode (server + web)

```sh
ASPNETCORE_ENVIRONMENT=Development dotnet run --project src/Project27.Server
p27 --server http://localhost:5240 --dev-user alice project create Alpha
p27 --server http://localhost:5240 --dev-user alice -p Alpha task add "Design" -d 2d
```

One editor at a time per project: `p27 checkout` holds the lock across
commands, `p27 checkin` releases it; owners can `p27 unlock` a stale lock.
Readers always see the last checked-in version. The web app (`web/`,
`npm run dev`) offers Gantt, network, timeline, and usage views with the same
checkout/edit/check-in flow, live via server-sent events.

`docker compose up --build` starts server + web for evaluation (DevAuth);
production requires OIDC — see README and `docker-compose.yaml` comments.

## 8. Install the CLI as a tool

```sh
dotnet pack src/Project27.Cli -c Release -o ./nupkg
dotnet tool install --global --add-source ./nupkg Project27.Cli
p27 --help
```
