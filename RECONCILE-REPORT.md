# Reconciling the search query ladder (#166) with per-indexer failure backoff (#170)

Branch `local/166-170-reconciled`, forked from `local/166-170-119-reconciled` (`30b553396`), carrying
`feat/166-search-query-ladder` (`109f4edf4`) with `feat/170-indexer-backoff` (`8955f4076`) merged into it.

Both branches forked from the same commit and both rewrote `IndexerSearchWorkflow.SearchIndexersAsync`,
so this is a merge with a real behavioural question in it rather than a textual one.

## What each branch did

**#166, the query ladder.** One search of one book used to produce exactly one query string per
indexer. If that string missed, the operator was told the book was not available, including where the
indexer demonstrably carried the release. The branch turns an audiobook into an ordered
`SearchQueryPlan` of query forms (title+author, title, title-stem+author, series+author, series) and
walks each indexer down the plan, stopping at the first form that finds something. Escalation is
narrow on purpose: the next form is only tried when `IndexerQueryObservation.ShouldEscalate` is true,
which means the indexer answered and had nothing. Anything else gets exactly one request.

**#170, the failure backoff.** Adds persisted per-indexer cooldowns: a widened outcome/reason
classification (`RateLimited`, `AuthFailure`, and a `RetryAfter` carried off the header), an escalating
ladder of cooldowns from 60s to 24h, a selection-time filter that drops a blocked indexer before it is
ever queried, an indexer-card surfacing in the settings UI, and a change to `AutomaticSearchService` so
a book searched against a reduced indexer set does not get its `LastSearchTime` stamped.

## The three conflicts, and how they were resolved

All three were in `listenarr.application/Search/Indexers/Common/IndexerSearchWorkflow.cs`.
`IndexerQueryObservation.cs` and `AutomaticSearchService.cs` auto-merged; both were re-read by hand and
are covered under "what survived" below.

### 1. The signature

Both branches added a parameter immediately before `CancellationToken ct = default`. Kept both, with
`plan` ahead of `filterBlocked`:

```csharp
SearchRequest? request = null,
SearchQueryPlan? plan = null,
bool filterBlocked = true,
CancellationToken ct = default)
```

That order is not arbitrary. `SearchService.SearchIndexersAsync` passes `plan` positionally as the
seventh argument, so putting `filterBlocked` first would have silently bound a `SearchQueryPlan` to a
`bool` slot, or failed to compile if it were lucky. Every other call site in the tree names its
arguments past `request` (checked: `SearchController.cs:313`, `SearchService.cs:111/137/156`, and the
test call sites), so nothing else is order-sensitive.

### 2. Where selection ends and the ladder begins

This is the one that mattered. 170's backoff filter sits directly after `GetEnabledAsync`; 166's plan
derivation sat in the same place. Rather than putting them side by side, the filter stays where 170 put
it and the plan derivation moved **below the two early returns**:

```
GetEnabledAsync
  -> record configuredCount
  -> drop indexers currently in failure backoff
  -> return mock results   if configuredCount == 0   (nothing configured)
  -> return empty          if indexers.Count == 0    (everything blocked)
  -> build the query plan
  -> fan out, ladder per indexer
```

So the answer to "does the backoff filter run before the ladder starts for a blocked indexer" is yes,
and structurally rather than incidentally: a blocked indexer is gone from the collection the fan-out
iterates, and an indexer set emptied entirely by backoff returns before a plan object is even
constructed. A blocked indexer costs zero requests, not one per rung.

The two empty-list cases stay on separate branches, which is 170's doing and worth not undoing. The
mock-results branch exists for an install with nothing configured yet. Reaching it because every real
indexer happened to be in cooldown would put five invented releases in front of the automatic-search
scorer and from there into a grab.

The "skipped, not queried" outcome 170 expects is unchanged: a filtered indexer never enters the
`observations` array, never reaches `RecordOutcomeAsync`, and never produces an observation of any
kind. `AutomaticSearchService`'s `AnyEnabledIndexerBlockedAsync` check is a separate question asked of
the status service after the search, so it is unaffected by how many rungs the ladder issued.

### 3. The fan-out body: one outcome per indexer, not one per rung

Resolved so that `RunQueryPlanAsync` runs the ladder and its single return value is what feeds the
backoff:

```csharp
var observation = await RunQueryPlanAsync(indexer, queryPlan, category, perIndexerRequest, ct);
observations[entry.Index] = (indexer, observation);
await RecordOutcomeAsync(indexer, observation, ct);
```

The concern was whether the ladder over-counts failures into the backoff. It does not, for two
reasons, and the second is the one that actually settles it.

The ladder returns exactly one observation however many rungs it issued, because
`RunQueryPlanAsync` returns the answer it stopped on. `RecordOutcomeAsync` is called from the fan-out
body, not from inside `SearchIndexerAsync`, so it sees that one collapsed answer. Four requests for one
book produce one call to `RecordAsync`.

Separately, at most one non-`Hit`/non-`NoMatch` observation can ever be produced per indexer per book
anyway: `ShouldEscalate` is true only for `NoMatch`, so a timeout, a refusal, a non-2xx or an
unreadable body halts the walk on the spot. The ladder cannot issue a second request to an indexer that
just failed.

Worth being explicit that the collapse matters in both directions, not just for failures.
`IndexerBackoffPolicy.Classify` maps `NoMatch` to `Success`, deliberately: an indexer saying it has
nothing is an indexer working. Had every rung's observation been recorded, a four-rung miss would have
sent four `Success` signals, and a walk that missed on rung 1 and timed out on rung 2 would have sent a
spurious `Success` alongside the `Escalate`. A failing indexer would have kept resetting its own
cooldown. One observation per indexer per book is correct for the escalation side and for the recovery
side.

## What survived the auto-merges

Checked by hand rather than assumed, since both branches edited `IndexerQueryObservation`:

- 170's `IndexerQueryReason.RateLimited` and `.AuthFailure` (lines 61, 64): present.
- 170's `TimeSpan? RetryAfter` record parameter and its `Unavailable(..., retryAfter)` overload
  (lines 87, 120-122): present.
- 166's rewritten `Tier` doc comment pointing at `SearchQueryPlan` (line 73): present. 166's only
  change to this file was that one doc line, which is why it merged without a conflict.
- 166's `observation with { Tier = tier }` stamping in `SearchIndexerAsync`, and the `tier` argument
  threaded through the three failure constructors: present.
- 170's `RecordOutcomeAsync` calls on the two operator-driven paths (`SearchByApiAsync`,
  `SearchIndexerResultsAsync`), which are deliberately not backoff-filtered because an operator naming
  one indexer has overridden the policy by asking: present.
- `AutomaticSearchService`: 166's `BuildSearchPlan` / `plan: searchPlan` and 170's `indexersSkipped`
  check and `AudiobookSearchOutcome` record both present and interleaved correctly. The `indexersSkipped`
  question is still asked after the search, so an indexer that entered backoff during this book's own
  fan-out still counts.

The three `RateLimited`/`AuthFailure` provider changes (Torznab/Newznab, Internet Archive, MyAnonamouse)
came across untouched; #166 modified no provider file.

A useful consequence of the two features meeting: a 429 now classifies as `Unavailable`, so
`ShouldEscalate` is false and a rate-limited indexer gets one request rather than four. The ladder makes
the rate-limit classification more valuable than it was on its own branch.

## Changes made beyond resolving conflicts

**`AutomaticSearchDegradedCycleTests` mock arity.** 170's `RunCycleAsync_NoIndexerSkipped_StampsLastSearchTimeAsBefore`
failed after the merge. Its `ISearchService.SearchAsync` setup was written against the six-parameter
signature; #166 added a seventh (`SearchQueryPlan? plan`), so the setup expression bound `plan` to a
literal `null` and stopped matching once `AutomaticSearchService` began passing a real plan. The mock
went unstubbed and the book never got stamped. Added `It.IsAny<SearchQueryPlan?>()`. This is a genuine
consequence of the interaction rather than a rename: on 170 alone the test was correct.

Note that the `SearchControllerTests` Moq setups keep their six-argument form and still pass, because
the controller path never passes a plan. Only the automatic-search path does.

**`IndexerSearchWorkflow.cs` split at the 500-line architecture guard.**
`BackendArchitectureTests.ActiveProductionSourceFiles_RemainFocused` caps a production source file at
500 lines. Base was 404; #166 alone reached 465 and #170 alone reached 469, both under. Merged, the file
was 538.

Per the standing rule, the check was not narrowed and no ignore was added. `GenerateMockIndexerResults`
(both overloads, 56 lines) moved to a new `IndexerMockResultGenerator` in the same namespace. It is the
largest self-contained block in the file and the one that least belongs there: it fabricates
placeholder data, where the rest of the class asks real indexers real questions. It had no dependency
on workflow state beyond the logger, and was called from exactly one place. `IndexerSearchWorkflow.cs`
is now 481 lines.

Flagging the headroom honestly: 481 leaves 19 lines before the guard trips again. The next change to
this file will probably need to extract something else. `GetFallbackIndexerName` (19 lines, already
`static`, no instance state) is the obvious next candidate and was left alone here only because a
reconcile is the wrong place to do refactoring nobody asked for.

**Three tests for the interaction itself**, added to `IndexerSearchWorkflowBackoffTests`. Neither
original branch could have written these, because on each branch alone the behaviour did not exist:

- `SearchIndexersAsync_BlockedIndexerWithAMultiRungPlan_CostsNoRequestsAtAll` — 170's existing skip
  test uses a one-form plan, where "skipped" and "asked once, answered nothing" both leave an empty
  result list, so it passes whether or not the ordering is right. With a four-rung plan the two
  separate: a filter running after the ladder would show four requests against the blocked indexer
  instead of none. The test carries its own control, asserting the healthy indexer really did walk all
  four rungs, so a workflow that queried nobody at all cannot pass it.
- `SearchIndexersAsync_LadderWalksEveryRung_RecordsOneOutcomeNotOnePerRung` — four requests, one
  recorded observation, outcome `NoMatch`.
- `SearchIndexersAsync_LadderHaltedByATimeout_RecordsTheTimeoutAndNotTheRungBeforeIt` — two of four
  rungs issued, one recorded observation, `Unavailable`/`Timeout`. Pins that the earlier `NoMatch` does
  not also arrive as a separate `Success`.

## Results

- `dotnet build tests/Listenarr.Tests.csproj` — succeeded, 0 warnings, 0 errors.
- `dotnet test tests/Listenarr.Tests.csproj` — **Passed: 3232, Failed: 0, Skipped: 130, Total: 3362.**
  (The 130 skips are the pre-existing Windows/platform-gated set, unchanged by this merge.)
- `dotnet format --verify-no-changes` — clean, exit 0.

**Frontend checks were not run, and here is why rather than skipping it quietly.** #166 touched no file
under `fe/` at all. The merged `fe/` tree is byte-identical to `feat/170-indexer-backoff`
(`git diff feat/170-indexer-backoff -- fe/` is empty), so the indexer-card surfacing carries zero merge
interaction. `fe/node_modules` is not installed in this worktree; running `npm ci` plus `test:unit`,
`type-check` and `lint` would re-verify 170's own branch rather than anything this merge changed. If the
frontend gate is wanted regardless, it should be run on `feat/170-indexer-backoff` directly, where it is
the same code.

## Not cleanly reconciled

Nothing was dropped, and no conflict was resolved by picking one side over the other. Two things to
carry forward rather than problems with the merge:

1. The 19 lines of headroom in `IndexerSearchWorkflow.cs` noted above.
2. `ISearchService.SearchIndexersAsync` exposes `plan` but not `filterBlocked`, so every caller
   reaching the workflow through the interface gets backoff filtering on. That is 170's design and the
   merge did not change it, but it does mean the only way to reach `filterBlocked: false` is to hold
   the concrete `IndexerSearchWorkflow`, which today is the workflow's own operator-override paths and
   the tests. Worth a deliberate decision if an API caller ever needs the override.

Neither has been validated on the running install. Per the standing rule, that is the next step before
any of this is offered upstream: this report covers a merge and a test suite, which is evidence about
the code doing what we thought we were testing, not evidence about a real library.
