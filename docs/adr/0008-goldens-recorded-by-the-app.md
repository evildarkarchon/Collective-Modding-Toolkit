---
status: accepted
amends: ADR-0007
---

# Goldens are recorded by the app

ADR-0007 kept the Parity Scenarios and their goldens as regression fixtures, but no golden could be re-recorded,
because only the retired Python driver could write one. Every change to a recorded result became a hand edit of the
JSON. #105 showed the cost: renaming one DEBUG log line meant editing 30 goldens through a throwaway script. From now
on, the C# app records the goldens itself, and a reviewer accepts a change by reading its diff.

**Decided**
- **One golden per scenario.** Each `golden.port.json` replaces its scenario's `golden.json`, and the overlay
  mechanism goes. A scenario's `deviations` entries stay as the port's record (ADR-0007), with no file of their own.
- **A mismatch records the full result.** When a replay doesn't match, the test writes what the app produced to
  `golden.new.json` beside `golden.json` and fails, listing every difference by JSON path. Accepting the change means
  replacing `golden.json` with `golden.new.json`. Setting `CMT_RECORD_GOLDENS=1` writes `golden.json` directly, for
  a change that touches many scenarios. `golden.new.json` is git-ignored, so only an accepted golden is committed, and
  the commit's diff is the review.
- **A recording keeps the golden format**: sorted keys, two-space indent, LF line endings and the `$unordered`
  marker. A replay that compares only the semantic fields keeps the golden's `display` sub-objects as they are.
- **Goldens don't record DEBUG lines from the app log.** About 94% of the app-log lines in the goldens were DEBUG
  traces. They change whenever code is reorganised, and no scenario is about one. INFO, WARNING and ERROR lines stay,
  because about 36 scenarios exist to check that a failure is logged (an update check that fails is logged and
  treated as no update, for example). The windows' visible Log panes are UI text and stay in full.

## Considered Options

- **Store a delta file** (`golden.new.json` holding only the changes). A delta goes stale as soon as `golden.json`
  changes, it needs its own tooling to apply, and it would bring back the two-file overlay this ADR removes. The
  readability it offers comes instead from the failure message, which lists the differences by path.
- **Drop the app log from the goldens entirely.** That loses the scenarios that check a failure is logged, unless
  each one is ported to a hand-written test. Dropping only DEBUG removes nearly all the churn and keeps that coverage.
- **Keep hand-editing the goldens.** Every cross-cutting change costs a script and a hard-to-review diff.

## Consequences

- The Python app's own recordings of the 61 overlaid scenarios leave the tree, but stay in history: `git log --
  'parity/scenarios/**/golden.json'` before this change.
- A recording can't tell a regression from an improvement. An approve-mode diff needs the same review as code.
- This replaces ADR-0007's Consequences bullet "No golden can be re-recorded, so a hand-written golden edit is now
  the normal way to update one."
