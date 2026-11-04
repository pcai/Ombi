# Ombi SQLite benchmarks

A small, standalone console harness (not part of `Ombi.sln`, not shipped)
that measures the SQLite write patterns Ombi actually runs, so a refactor
can be judged against numbers instead of intuition. It never touches a
real Ombi database: every run uses throwaway files under `scratch/`.

## Prerequisites

The .NET SDK pinned by the repo's `global.json` (8.0.x). No other setup:
restore happens automatically on first run.

## Run

From the repo root:

```
dotnet run -c Release --project benchmarks/Ombi.SqliteBenchmarks
```

Options:

- `--iterations N` — timed runs per scenario, default 3. A warmup run is
  always done first and discarded; the reported number is the median.
- `--only <scenario>` — run one scenario:
  `availability`, `whole-graph-update`, `refresh-metadata`, `contention`,
  `lookup`. Without it, all five run.
- `--out DIR` — where to write results, default `benchmarks/results`.

A run writes `report.md` and `results.csv` to `--out`, plus a `scratch/`
directory of throwaway database files, deleted at the end of a
successful run (safe to delete at any time).
Each scenario run uses a fresh, uniquely named file, so runs never
interfere with each other.

A full run takes on the order of 10-15 minutes on a slow disk, dominated
by the per-item variants (hundreds of fsync-bound transactions each).
`--only` is the way to iterate on one scenario.

## Checking in a new baseline

`report.md` and `results.csv` in `benchmarks/results/` are gitignored
working files. The checked-in baseline is a dated copy:

```
cp benchmarks/results/report.md benchmarks/results/baseline-YYYY-MM-DD.md
cp benchmarks/results/results.csv benchmarks/results/baseline-YYYY-MM-DD.csv
```

Re-running overwrites `report.md`/`results.csv`, never the dated copy.
When a refactor lands, re-run, compare against the previous dated
baseline, and check in the new dated pair so the next change has
something to measure against. The report header records the run date,
host, .NET version, and iteration count.

## Reading the results

Each scenario runs two variants: the old pattern and the improved one.
Three things matter, in this order:

1. **The ratio between variants**, not the absolute milliseconds.
   Absolute times depend heavily on the host's disk: on a VM with slow
   fsync, one SQLite transaction commit can cost ~80 ms, which inflates
   every per-item variant enormously. The ratio is the signal, but it
   varies by host too (the lookup pair measured 13.3x on Linux and 3.7x
   on Windows), so compare runs on the same host; the report header
   records the host.
2. **`SQLITE_BUSY` count** (contention scenario). A busy error in
   production is a failed save or a swallowed update, so zero busy
   errors under the improved configuration is the headline there, ahead
   of the time.
3. **Rows written** (sum of `SaveChanges` return values). For
   `whole-graph-update` it shows how many rows a one-field change
   rewrites: wall time barely differs between variants there because
   both are a handful of fsync-bound transactions, but the row count
   differs by an order of magnitude.

Pattern scenarios (`availability`, `whole-graph-update`,
`refresh-metadata`, `lookup`) run under a fixed pre-refactor
configuration (DELETE journal, busy_timeout 0) so the pattern effect
is measured separately from the configuration effect. Only
`contention` compares configurations. Contention is a single timed
parallel run (parallel timing is not stable enough to median
meaningfully).

These are worst-case pattern measurements, not job runtimes: the
availability scenario matches all 500 movies and saves up to three
times per movie (the real code saved at most twice, and only for
matched movies); refresh-metadata excludes the network lookups the
real job performs; and the contention pair mixes WAL with the
per-commit fsync drop from synchronous NORMAL (which survives a
process crash but can lose the last transactions on OS crash or power
loss).

## What is measured, and what it maps to in the app

Each pair compares the pattern the code used to run before the
2026-11 SQLite refactor with the improved pattern (for
`whole-graph-update`, the detached-`Db.Update` shape is still what
`TvRequestRepository.Update` does for detached callers; the stub is
the pattern `MarkAsAvailable` already uses). The harness keeps both
variants so a future change can still be measured against the old
shape.

| Scenario | Variants | Real code |
|---|---|---|
| `availability` | SaveChanges per movie (up to 3x) vs one save at end | `src/Ombi.Schedule/Jobs/Plex/PlexAvailabilityChecker.cs` — the old shape streamed an unmaterialized query and saved per movie. The Emby/Jellyfin checkers already did the batched shape, and Plex now matches them. |
| `whole-graph-update` | `Db.Update(detached graph)` vs stub + `IsModified` on one property | `TvRequestRepository.Update` / `UpdateChild`. Two distinct costs, both real: same-scope callers (`UpdateRootPath`/`UpdateQualityProfile` load via `Get()` then `Update` in one scope) pay the full-graph *load* to change one field — EF marks only the root Modified there, which this harness verified (1 row per save in the tracked case). Detached callers pay the full-graph *rewrite*: `Db.Update` marks every reachable entity Modified. The stub pattern already exists in-repo (`MarkAsAvailable`, `MarkChildAsAvailable`) and avoids both. |
| `refresh-metadata` | SaveChanges per item vs one save at end | `src/Ombi.Schedule/Jobs/Ombi/RefreshMetadata.cs` — the old shape saved per movie/show across all six `Start*` methods, on the External DB; it now saves every 100 items plus a final save per pass. |
| `contention` | Same parallel-writer workload under the pre-refactor config vs WAL + busy_timeout 5000 + synchronous NORMAL | The configuration in `src/Ombi/Extensions/DatabaseExtensions.cs` (`ConfigureSqlite`): the old shape was a bare `Data Source=...` — DELETE journal mode, busy_timeout 0, no retry — while the Postgres/MySQL branches get `EnableRetryOnFailure`. Several Quartz jobs write the same file with no mutual exclusion. |
| `lookup` | Point lookup by `TheMovieDbId`, no index vs indexed | Before the refactor, snapshots indexed FKs and `RequestedUserId` but not the keys sync/availability code looks up by (`MovieRequests.TheMovieDbId`, `TvRequests.ExternalProviderId`, content `Key`/`ImdbId`/`TheMovieDbId`). The lookup-index migration added them, and a follow-up correction removed the Type index and three indexes that duplicated alternate-key unique constraints. |

## Adding a scenario

1. Add the model shape to `Models.cs` if the scenario needs a new
   entity (keep models minimal mirrors of the real entities, not
   `Ombi.Store` entities, so Identity and the full migration history
   don't dominate the measurements).
2. Add a method to `Scenarios.cs` using the `Run` helper: it takes a
   scenario name, a variant name, a seed action, and the work to time.
   `Run` handles the warmup, the fresh file per run, the timing, and
   the rows-written count. Add the new scenario in both variants
   (old pattern vs improved pattern) so the ratio stays measurable.
3. Register it in `Program.cs` (name, row counts in the header, and
   the `--only` key).
4. Document it in the table above, with the real code it maps to.

## Deliberate limitations

- The models are minimal mirrors of the real entities, not `Ombi.Store`
  entities, so Identity and the full migration history don't dominate the
  measurements. Row counts are smaller than a large production library;
  the *ratios* between variants are the signal, not the absolute
  milliseconds.
- Pattern scenarios run under the baseline configuration so the pattern
  effect is isolated; only `contention` compares configurations.
- Contention is a single timed parallel run (parallel timing is not
  stable enough to median meaningfully); its headline number is the
  `SQLITE_BUSY` count.
- The harness uses EF Core SQLite 8.0.5, the same version as
  `Ombi.Store`, and the same per-context connection discipline the app
  uses (one connection per context, one context per job scope).
