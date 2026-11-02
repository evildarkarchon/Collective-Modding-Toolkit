---
status: accepted
---

# UI Actions run through one runner

The #94–#101 sweep made `ErrorReporter.Handle` the single way to start a UI Action, but every caller still built the
rest of the flow by hand. Callers held their own input blocks across several Operations, caught
`OperationFailedException` to continue after a failure, and moved progress to the UI thread in three different ways.
The rule that the Error Window opens before deferred input is replayed held only because separate mechanisms happened
to fire in the right order. From now on one module, `UiActions` (`runtime.Actions`), starts every UI Action and every
Operation and owns that ordering. ADR-0003 is unchanged: every Operation is still Blocking or Interactive.

**Decided**
- **One entry point.** `Start` and `Schedule` replace `Handle`, and `Schedule` also replaces the Downgrader's re-post
  of its download poll. `Run` and `RunPhased` replace the public `BackgroundOperations`. No forwarding shims are left,
  so there is again only one way into a UI Action. Modals that are shown and not awaited also go through `Start`, so
  the README's "never `_ = XAsync()`" rule has no exceptions.
- **No action object.** Every runner method can be called anywhere on the UI thread. An Operation is not tied to the
  UI Action that started it, so `OverviewState`, `SettingsStore` and the Downgrader's nested Overview refresh keep
  their signatures.
- **Blocking spans report before they release.** `Blocking(span)` holds the input block across several Operations.
  If the span fails with an error the runner has not reported yet, the span reports it, then releases the block, then
  rethrows it as already reported. That way the Error Window is always up before deferred Close or Escape replays.
- **A failure still stops the action by default.** `Run` throws, as before. `TryRun` returns an `OperationOutcome`
  (`Succeeded` or `Failed`, with the error already reported) for the few callers that must continue after a failure.
  A caller that continues and then has to stop calls `ThrowIfFailed()`, which ends the action without reporting the
  failure again. `ReportFailure` reports an error that the work returned rather than threw.
- **The Overview Report module follows the same rule.** `OverviewState` keeps its throwing `LoadAsync` and
  `RefreshAsync` and adds `TryLoadAsync` and `TryRefreshAsync`, because three of its four callers continue after a
  failed gathering. A failed gathering still leaves its partial report current, whichever form is called. With that,
  no caller catches `OperationFailedException`, and it becomes internal to the App.
- **Progress uses one kind of sink.** Callers create a `UiThreadProgress` sink, which applies each event on the UI
  thread before the work continues. The runner does not own the sink: the Downgrader's sink outlives the Operation that
  created it, because Core hands it on to each scheduled download poll.
- **The busy flag stays outside the runner.** The Downgrader drives its close guard from Core's `QueueRunning` events,
  which the runner can't see.

## Considered Options

- **Deepen only the operation runner and leave `Handle` in `ErrorReporter`.** The ordering rule would stay split
  between the two modules, because only the module that both reports and releases the block can guarantee it.
- **Pass an action object to everything that starts an Operation.** That would show which UI Action owns each
  Operation, but it would widen `OverviewState`, `SettingsStore` and the Downgrader's host, and every test that starts
  an Operation. It would also give no extra guarantee once spans report before they release.
- **Return typed results from every Operation.** Every caller would have to check for failure, and the glossary's
  "a UI Action stops at its first failure" would become something each caller must remember.
- **Unify Core's two partial-result conventions.** The Overview gathering throws `OverviewIncompleteException` carrying
  its partial report, while the Scan returns its error on its outcome. Unifying them changes Core's API without making
  the runner any deeper. `Failed` carries the original exception, so the Overview still finds its partial report by
  the exception's type.
- **Have the runner create a sink for each Operation.** The sink would drop events reported after its Operation ended,
  which would lose every event from the Downgrader's download polls.

## Consequences

- With a waiting sink, the Scanner no longer drops statuses from an older scan once a newer one starts. That changes
  only the symptoms of #116, where a second scan can start on top of the first.
- `OperationFailedException` remains as the runner's internal "already reported, stop the action" signal. The global
  handlers still drop it.
