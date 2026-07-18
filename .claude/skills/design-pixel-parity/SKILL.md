---
name: design-pixel-parity
description: Use when implementing a Claude Design handoff (pixel-parity fixes, "make the app match the mockup") for project27's web app. Covers fetching the handoff doc, reading ground truth from the .dc.html source instead of trusting screenshots, standing up a real local environment to verify against, and how to avoid "fixing" things that already work.
---

# Design pixel-parity handoffs (project27/web)

Handoff docs describe symptoms from screenshots. Screenshots can be stale, can
catch a transient state (focus, autofocus, one data sample), or can just be
wrong. **Never patch code based on the doc's prose alone — reproduce the bug
in the running app first, then fix it, then re-verify in the running app.**
Every real bug found in past passes was confirmed this way; every false
positive (see "Known false positives" below) was one where the description
sounded plausible but the running app proved otherwise.

## 1. Fetch the handoff and its ground truth

The handoff lives in a Claude Design project, not just the chat. Use the
`DesignSync` tool:

```
DesignSync method=get_project projectId=<uuid>        # confirm it's the right project
DesignSync method=list_files projectId=<uuid>          # find "Handoff - ....md" and the *.dc.html ground truth
DesignSync method=get_file path="Handoff - ....md"
DesignSync method=get_file path="Main.dc.html"         # or whichever option the handoff names as ground truth
```

The `.dc.html` file is the real source of truth — it has literal inline
`style="..."` strings with exact pixel values (heights, padding, font-size,
`border-radius`, etc.). Grep it instead of eyeballing the screenshot:

```bash
grep -n "ghostBtn\|projNameStyle\|rowStyle\|ROW =" /tmp/main_dc.html
```

This is how exact numbers were found last time: top bar 44px, action bar
38px, sheet header 30px, `ROW=26`, `ghostBtn` padding `3px 8px` @ 12px font,
row cells IBM Plex Mono at 11px for Dur/%/Slack, project title 14px/600.
Don't guess these — read them out of the mock's style strings.

## 2. Stand up the real environment before touching code

You cannot judge pixel parity from source reading alone (this codebase's
`.view-switch button` already had `border:none;border-radius:0` in source
while a screenshot showed a rounded pill — the screenshot was stale). Render
the actual app:

```bash
# Terminal 1: server (Development enables DevAuth)
ASPNETCORE_ENVIRONMENT=Development ASPNETCORE_URLS=http://localhost:5240 \
  dotnet run --project src/Project27.Server

# Terminal 2: web dev server
cd web && npm run dev
```

**Gotcha**: if `P27_SERVER` (or `P27_PROJECT`) is set in your shell env, Vite's
`/api` proxy silently points at that remote server instead of localhost:5240,
and every request 401s with no obvious reason (the response even carries
`server: cloudflare` headers from the *remote* deployment). Always launch the
web dev server with `env -u P27_SERVER -u P27_PROJECT npm run dev`, and if
`/api/me` 401s through the proxy, `curl http://localhost:5173/api/me` and
check the response headers before assuming it's an auth bug.

Seed a real project with **varied, linked** data — not a pile of identical
same-day tasks, which will visually cluster and look like a bar-geometry bug
that isn't one:

```bash
run() { env -u P27_SERVER -u P27_PROJECT dotnet run --project src/Project27.Cli -- \
  --server http://localhost:5240 --dev-user alice -p "Design Demo" "$@"; }
run project create "Design Demo"
run checkout
run task add "Discovery"
run task add "Stakeholder interviews" -d 3d
run task add "Requirements draft" -d 4d
run link add 1 2
run link add 2 3
run task set 3 --percent-complete 60
run resource add "Lee"
run assign add 3 Lee
run baseline set
```

Clean up when done: `run checkin` then `run project delete "Design Demo"`,
and kill both dev processes.

## 3. Screenshot the real thing with headless Chromium

`playwright-core` may already be a transitive dep with no top-level package
(check `ls node_modules/.bin | grep playwright`); if `import { chromium } from
'playwright-core'` fails with `ERR_MODULE_NOT_FOUND`, `npm install
playwright-core@<version> --no-save` in a scratch dir works (network is
available). Find the cached browser binary rather than trying to download
one:

```bash
find ~/Library/Caches/ms-playwright -type f -perm -u+x 2>/dev/null | grep -i "Chrome for Testing$"
```

Then drive it with a small script (log in via the Dev-user radio, click
"Sign in", navigate to the seeded project, screenshot). Useful patterns:

- Screenshot a specific element (`page.locator('.action-bar').screenshot(...)`)
  and then crop/zoom with PIL for a close look, rather than full-page shots
  you then squint at.
- For "is this control really broken or just mid-interaction" questions
  (e.g. a focus ring, a hover state), deliberately drive both states
  (`autoFocus` firing vs. clicking elsewhere to blur) rather than judging
  from one screenshot.
- For exact sizing questions, don't eyeball pixels — use
  `page.evaluate(() => el.getBoundingClientRect())` and `getComputedStyle`.
  This is how the icon-only-vs-text-button height mismatch (20px vs 23px,
  from SVG intrinsic height vs. text line-height) was actually found, not by
  looking at a screenshot.
- Test the boundary conditions the handoff cares about: add enough rows/columns
  to force scroll, switch themes (dark/light), switch away from the view being
  fixed to confirm you didn't regress other views (e.g. inspector overlay
  behavior in Table/Network vs. docked behavior in Gantt).

## 4. Root-cause pass before spot fixes

If the handoff has a "root cause" section (shared tokens like border-radius,
fonts, sizing), do that pass first with `sed -i` across the CSS file — it
collapses a dozen "separate" bugs into one edit. Example from this project:
every non-avatar `border-radius` should be `var(--radius-none)`, and the fix
was one `sed` sweeping `var(--radius-sm)`, `var(--radius-md)`, `999px` →
`var(--radius-none)`, careful not to touch `--radius-round` (avatars).

## 5. Known false positives — verify, don't assume

These looked like real bugs from the handoff's screenshots/prose but were
proven correct-as-is once rendered and interacted with:

- **Bar geometry "clustering"**: `xOf(scale, task.start)` /
  `barWidth` were already per-task correct; the screenshot's identical-looking
  bars were unlinked, same-day seed data, not a rendering bug. Confirmed by
  seeding *linked, staggered* tasks and re-screenshotting.
- **"Empty" tracking % control**: turned out to genuinely be broken once
  checked properly (`color: transparent` on the tick labels) — so "looks
  empty in one screenshot" isn't proof either way; check the actual DOM/CSS
  for the specific element, not just what render s in one screenshot.
- **Textarea border "always heavy"**: it was the `autoFocus` prop firing —
  correct `:focus`-only behavior, confirmed by blurring and re-screenshotting.

The pattern: **before changing code, form a specific hypothesis about the DOM/
CSS cause, then check that exact thing** (computed style, a second screenshot
in a different state, seeded data that isolates the variable) rather than
pattern-matching the prose description to a plausible-sounding fix.

## 6. When the user pushes back on a fix

Revert *only* the specific complained-about part, not everything from that
change. E.g. when abbreviated column headers ("Dur"/"Slk") looked bad in
practice, only the label strings were reverted — the mono font and
right-alignment on those same columns (which nobody objected to) stayed.

## 7. Before declaring done

- `npm run build && npm run lint && npm test` in `web/` — required, but does
  **not** verify pixel parity by itself. Screenshots/measurements are the
  actual verification for this class of task.
- Re-screenshot every changed area after the fix, not just the one you
  expect to have changed — CSS root-cause passes (e.g. a shared button class)
  can move things you didn't explicitly touch.
- Don't commit or push unless the user asks; when they do, one commit for
  the whole handoff pass is reasonable, with a body that lists the fixed
  items and explicitly calls out anything investigated-and-left-alone (so
  it doesn't get "fixed" again in a future pass).
