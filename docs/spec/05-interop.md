# Phase 5 — Interop (ran after phase 12, as postponed)

MSPDI XML both ways, CSV export-only (decision D3). Lives in
`Project27.Interop` (Core + nothing else).

## 5a — CSV export

`CsvExporter.Write(project, viewDefinition)` renders any view-engine result
(same tables/fields/filters/sorts as `p27 view`) to RFC-4180 CSV: quoted where
needed, CRLF, formatted cell values (the raw layer stays JSON's job). Grouped
views emit a `Group` column instead of heading rows.

```
p27 export csv [--out file.csv] [--table entry|…] [--fields …] [--filter …] [--sort …] [--group-by …]
```

## 5b — MSPDI XML

`MspdiWriter.Write(project)` / `MspdiReader.Read(xml)` over the documented
Microsoft Project Data Interchange schema (`http://schemas.microsoft.com/project`).

Scope: project attributes and time settings, calendars (week days + dated
exceptions), the task outline (1-based `OutlineLevel` per deviation #10,
durations as ISO-8601 `PT…H…M…S`, constraints, deadlines, priorities, percent
complete, actual/manual dates, milestones, notes-free), dependency links with
lag (tenths of minutes per MSPDI), resources with standard/overtime rates and
max units (percent × 100), and assignments with units/work. Baselines export
slot 0. Custom fields, contours beyond flat, splits, and recurring markers do
not round-trip (deviation #32); unknown inbound elements are ignored.

Round-trip tests assert schedule equivalence (dates, slack, costs) after
export → import → recalculate.

```
p27 export mspdi [--out file.xml]
p27 import mspdi <file.xml> [--file new.p27]
```

`import mspdi` also works in `--server` mode: it POSTs the XML to
`/api/projects/import/mspdi` and creates a new server project instead of a
local `.p27` (`--file`/`P27_FILE` has no meaning there — it errors rather than
being silently dropped). `import p27 <file.p27>` is server-mode only — it
POSTs the SQLite file to `/api/projects/import/p27` for cases where copying
the `.p27` file directly isn't practical (e.g. scripting against a server
that isn't reachable over the filesystem).
