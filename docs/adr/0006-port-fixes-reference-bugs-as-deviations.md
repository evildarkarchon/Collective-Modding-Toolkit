---
status: accepted
supersedes: the bug clause of ADR-0001
---

# The port fixes reference bugs, each as a documented Deviation

ADR-0001 had the port reproduce the Reference Implementation's bugs and log them for later, so that parity stayed checkable. The user decided on 2026-10-12 that the C# app should fix every open `bug` issue instead of shipping it again. So each fix lands as a **Deviation**: an explicit, reviewable override of Behaviour Parity, tied to its `bug` issue by number, with a one-line reason. Everything a Deviation doesn't name stays at parity with the Parity Baseline.

ADR-0004's proof model is unchanged. The reference recording stays the source of truth. A Parity Scenario's `golden.json` is still what the unmodified reference produced, and `record.py --check` still verifies it. A Deviation adds to the scenario rather than editing it: it declares `deviations` (bug number and reason) in `scenario.json` and carries a hand-written `golden.port.json` with the port's expected results beside the reference golden. The C# replay compares against the port golden when there is one. Hand-written tests that asserted the buggy behaviour keep their Parity ID and gain a `Deviation` attribute that names the bug. The coverage check lists every Deviation on each run, and fails on one that has a malformed bug number, no reason, or no port golden.

## Considered Options

- **Keep reproducing bugs, and fix them after Cutover.** This keeps the oracle simple, but it ships known bugs in the first C# release and repeats every fix against a codebase that no longer has the reference to compare with.
- **Fix bugs and re-record or hand-edit `golden.json`.** This leaves one golden per scenario, but the golden stops being a recording of the reference. `--check` would then fail forever, or need exemptions, and nothing would show where the port departs from the reference.
- **Fix bugs inline during the slices.** ADR-0001 rejected this, and the reason still holds while slices are being built: a slice proves parity first. Fixes happen in a separate fix phase after every slice has merged, one bug per commit, so each Deviation can be reviewed against a parity proof that was already green.

## Consequences

- Behaviour Parity now means parity except where a Deviation says otherwise. A scenario with a Deviation still proves its Parity Inventory IDs, and those IDs (the `B-` ones included) now prove the fixed behaviour.
- After Cutover the driver is gone, so `golden.json` and `golden.port.json` both become plain regression fixtures. The pair still records what the reference did and what the port does instead.
- Failure Outcomes that come from a bug are no longer automatically part of parity. Once a Deviation fixes one, the port's outcome is the one in the port golden.
