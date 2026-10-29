---
status: accepted
supersedes: ADR-0004 and ADR-0006; amends ADR-0001, ADR-0002 and ADR-0003
---

# The parity process is retired; the scenarios stay as regression tests

The port reached Behaviour Parity and replaced the Python app at the Cutover (0.7.0,
[#71](https://github.com/evildarkarchon/Collective-Modding-Toolkit/issues/71)). On 2026-10-29 the user retired the
parity process. From here the app moves forward with bug fixes and new features, each judged on its own merits rather
than against what the Reference Implementation did.

**Removed**
- The Parity Inventory (`docs/parity-inventory.md`), the coverage check (`dotnet/tools/CMToolkit.ParityCoverage`) and
  its CI step, and the ratchet files it read (`parity/covered.txt`, `parity/coverage-manual.json`).
- The requirement that a change to a recorded result be logged as a Deviation (ADR-0006), with a `deviations` entry and
  a `Deviation` test attribute.

**Kept**
- The Parity Scenarios, both kinds of golden, the replay harness and the screenshot pairs, as ordinary regression
  fixtures. When a fix or feature changes what a scenario must produce, the change edits that scenario's golden
  (`golden.port.json` if it has one, otherwise `golden.json`) in the same commit. `parity/README.md` says how.
- The existing `deviations` entries, `Deviation` attributes and `Trait("Parity", "<ID>")` traits, as the port's record
  of what it fixed and what each test was written for. New tests and changes don't add them.
- The names in code (`ParityScenario`, `CMT_PARITY_SCENARIO`, `Parity/` folders). Renaming them is churn with no change
  in behaviour; it can happen on its own later.

**Amended**
- ADR-0003's rule stands: every operation is either input-blocking or interactive. A new operation is classified by
  what it needs (whether input arriving mid-operation could make it wrong), not by what the Reference Implementation
  did.
- ADR-0002's `/CETCOMPAT:NO` stands, now as a product requirement: the app is still launched from MO2.
- ADR-0001's drop-in compatibility (`cm-toolkit.exe`, the `settings.json` keys, the folder layout) stands while users
  upgrade over 0.6.x installs. Changing it is now a product decision, not a parity break.

## Considered Options

- **Keep the coverage check as a regression guard.** It guards checklist IDs that describe the Python app, so every
  change would still have to be argued in parity terms. The tests themselves are the guard.
- **Delete the scenarios as well.** They are the widest regression net the app has, covering inputs that are hard to
  rebuild by hand, and the README's screenshots come from beside them.
- **Rename the harness now** (`Parity*` to `Scenario*`). It touches well over a hundred files for no change in what
  runs, so it is left for later.

## Consequences

- Code comments still cite checklist IDs (`SHELL-5`, `OVW-12`, `T-15`, `B-4`, ...) and the ADRs this one supersedes.
  The inventory is in history: `git show 2b8cea1:docs/parity-inventory.md`.
- The Python app at the `python-reference` tag is still useful for seeing how something used to behave. It is a
  reference, not a requirement: where its behaviour is a bug or just worse, the app can change it.
- No golden can be re-recorded, so a hand-written golden edit is now the normal way to update one. Review it like code.
