# 166a — per-indexer tiered query ladder

Branch `feat/166-search-query-ladder`, based on `local/166-170-119-reconciled` (`30b553396`).
Implements section 3 of `.local/design/166-search-query-ladder.md`, narrowed per the operator's
answer to that doc's question 2 (build now, do not wait on `trial/stack-2026-09-05`).

## What landed

New types in `listenarr.application/Search/Core/`:

- `SearchQueryPlan.cs` — `SearchQueryFormKind`, `SearchQueryForm(Tier, Query, Kind)`,
  `SearchQueryPlan(Forms)` with `Verbatim(query)`, `PrimaryQuery`, `IsVerbatim`, and an internal
  `FromCandidates` that drops blanks and repeats and numbers what survives.
- `AudiobookSearchQueryBuilder.cs` — `BuildPlan(Audiobook)`, plus `BuildQueryTitle`,
  `ContainsPhrase` and `Tokenize`.

Wiring:

- `IndexerSearchWorkflow.SearchIndexersAsync` takes `SearchQueryPlan? plan = null` and walks the
  plan per indexer in `RunQueryPlanAsync`, inside the existing bounded fan-out. A null or empty
  plan becomes `SearchQueryPlan.Verbatim(query)`, so every caller that does not pass one behaves
  exactly as before.
- `ISearchService.SearchAsync` / `SearchIndexersAsync` carry the plan through `SearchService`.
- `AutomaticSearchResultClassifier.BuildSearchQuery` became `BuildSearchPlan`, and
  `DownloadSearchQueryBuilder.Build` became `BuildPlan`; both forward to
  `AudiobookSearchQueryBuilder`. Their two call sites pass the plan down and log
  `plan.PrimaryQuery`.

## The ladder

For a book with a subtitle and a series not already in the title:

| Tier | Kind | Example (`She: A History of Adventure`, Haggard, series Ayesha) |
|---|---|---|
| 1 | TitleAuthor | `She: A History of Adventure H Rider Haggard` |
| 2 | Title | `She: A History of Adventure` |
| 3 | TitleStemAuthor | `She H Rider Haggard` |
| 4 | SeriesAuthor | `Ayesha H Rider Haggard` |
| 5 | Series | `Ayesha` |

Series rungs are dropped entirely when `ContainsPhrase(queryTitle, series)` holds, so
`The Wonderful Wizard of Oz` / series `Oz` is a two-rung plan. `Sherlock Holmes: A Study in
Scarlet` / series `Sherlock Holmes` is three rungs, and reaches the series name through the stem.
A book with no series and no subtitle is the doc's tier 1 and 2 only. The doc's own worked
example, `Heaven's River` / `Dennis E. Taylor` / `Bobiverse`, is exactly the doc's four rungs.

## Judgment calls the design doc did not settle

1. **`AudiobookSearchQueryBuilder` did not exist on this base.** It lives only on
   `trial/stack-2026-09-05`, which the operator ruled out as a dependency, yet the scope names the
   class and its `BuildQueryTitle`. Created it here instead of cherry-picking, with
   `BuildQueryTitle`, `ContainsPhrase` and `Tokenize` copied character for character from
   `5cbb27bd` so the documented conservative parenthesis handling is preserved and a later landing
   of that commit is a textual identity rather than a semantic conflict. The plan-building half is
   new. The unification of the two divergent builders comes along with this unavoidably: the ladder
   has to fire on both book-record paths, and both paths' tier 1 is the same string once the series
   suffix stops being appended.
2. **Where the subtitle stem sits, and whether it gets a bare rung.** The doc says the stem is an
   additional rung and does not say where. Placed after both full-title rungs and before the series
   rungs, and emitted only paired with the author. A bare `She` or `Sherlock Holmes` with no author
   anchor is the query the doc calls near-useless, and the bare series rung already occupies the
   wide end of the ladder. So the plan never issues a lone stem.
3. **`SearchQueryFormKind.TitleStemAuthor` added** to the doc's five-value enum, for that rung.
4. **Only `:` splits a stem**, not `(`. `BuildQueryTitle`'s parenthesis handling is untouched, as
   instructed.
5. **Initials normalisation is `(?<![\p{L}\p{N}])(\p{L})\.` → `$1 `**, applied to the author only,
   in the builder. A lone letter before a full stop is an initial; `Jr.` is not, and survives. The
   sanitizer is untouched, so operator-typed text still reaches the wire as typed.
6. **`RunQueryPlanAsync` is `internal` rather than private** so the tests can assert on the outcome
   and the tier it stopped at. `SearchIndexersAsync` returns a flattened result list, which is the
   collapse the observation exists to undo, so there is no other way to assert the doc's "Hit at
   tier 3" and "Unavailable after one request". `Listenarr.Application` already has
   `InternalsVisibleTo("Listenarr.Tests")`.
7. **The plan owns tier numbering.** `SearchIndexerAsync` takes a `tier` and stamps it onto the
   observation with `observation with { Tier = tier }`; a provider answering one query cannot know
   which rung it is on.

## Deliberately not built

Per the doc's section 7 and the task's scope:

- `t=caps` fetching or caching. Nothing in this branch issues one.
- Whitelisting the apostrophe in `IndexerQuerySanitizer`. The forbidden-character constant is
  unchanged, so `Heaven's River` still goes out as `Heavens River`, which is what the ladder tests
  assert against.
- Any operator-facing surface for the outcome. It is still log lines plus the typed observation.
- Readarr's "keep part 0 of the title, discard the rest". The full title stays at tier 1.
- Any `isAutomaticSearch` gate on ladder length (the doc's question 5, deferred to #170).

## Tests

`tests/Features/Application/Search/Core/AudiobookSearchQueryPlanTests.cs` (14) and
`tests/Features/Application/Search/Indexers/IndexerSearchWorkflowLadderTests.cs` (11, one of them a
five-case theory). All seven tests named in the doc's section 8 are present, with test 3 and test 4
driving `RunQueryPlanAsync` so they can assert the outcome and tier, and test 5 split into the
fan-out request-count assertion and a theory covering every non-escalating outcome.

Full suite: 3169 passed, 0 failed, 130 skipped, 3299 total. `dotnet format --verify-no-changes`
clean. `scrub.py --check` clean over the diff.

## Not done here

Not validated on the running install. There is nothing to carry into `local_stack.extra` from this
worktree yet, and the standing rule wants the observed-working round before any of this is offered
upstream.
