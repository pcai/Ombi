// Usage (from the repo root):
//   dotnet run -c Release --project benchmarks/Ombi.SqliteBenchmarks -- [--iterations 3] [--out benchmarks/results] [--only availability|whole-graph-update|refresh-metadata|contention|lookup]
// Output: a table on stdout, plus report.md and results.csv in --out.
// The checked-in baseline is benchmarks/results/baseline-YYYY-MM-DD.md,
// a copy of the report from the run that established it; re-running
// overwrites report.md/results.csv but never the dated baseline copy.
//
// Every scenario runs on a fresh SQLite file in a scratch directory under
// --out (deleted and recreated per run), so results are not polluted by
// previous runs or by the machine's own Ombi databases. Nothing here
// touches a real Ombi installation.

using System.Runtime.InteropServices;
using Ombi.SqliteBenchmarks;

public static class Program
{
    public static int Iterations = 3;

    public static int Main(string[] args)
    {
        var only = "all";
        var outDir = Path.Combine("benchmarks", "results");
        for (var i = 0; i < args.Length; i++)
        {
            if (args[i] == "--iterations" && i + 1 < args.Length) Iterations = int.Parse(args[++i]);
            else if (args[i] == "--out" && i + 1 < args.Length) outDir = args[++i];
            else if (args[i] == "--only" && i + 1 < args.Length) only = args[++i];
        }

        var scratch = Path.Combine(outDir, "scratch");
        Directory.CreateDirectory(scratch);
        var results = new List<Result>();

        void Add(Func<string, Result> fn, string key)
        {
            if (only == "all" || only == key)
            {
                var r = fn(scratch);
                results.Add(r);
                Console.WriteLine($"{r.Scenario,-18} {r.Variant,-42} median {r.MedianMs,10:F1} ms  busy {r.BusyErrors}  rows {r.RowsWritten}");
            }
        }

        Console.WriteLine($"Ombi SQLite benchmarks: iterations={Iterations} (warmup discarded, median reported)");
        Console.WriteLine($"Rows: {Scenarios.MovieRows} movies, {Scenarios.TvRoots} TV roots (x5 children x10 episodes), " +
                          $"{Scenarios.ContentRows} content rows, {Scenarios.LookupRows} lookup rows, " +
                          $"{Scenarios.Writers} writers x {Scenarios.WritesPerWriter} inserts");
        Console.WriteLine();

        Add(Scenarios.AvailabilityPerItem, "availability");
        Add(Scenarios.AvailabilityBatched, "availability");
        Add(Scenarios.WholeGraphUpdate, "whole-graph-update");
        Add(Scenarios.StubPropertyUpdate, "whole-graph-update");
        Add(Scenarios.RefreshPerItem, "refresh-metadata");
        Add(Scenarios.RefreshBatched, "refresh-metadata");
        if (only == "all" || only == "contention")
        {
            var b = Scenarios.Contention(Config.Baseline, scratch);
            results.Add(b);
            Console.WriteLine($"{b.Scenario,-18} {b.Variant,-42} {b.MedianMs,10:F1} ms  busy {b.BusyErrors}  rows {b.RowsWritten}");
            var w = Scenarios.Contention(Config.Improved, scratch);
            results.Add(w);
            Console.WriteLine($"{w.Scenario,-18} {w.Variant,-42} {w.MedianMs,10:F1} ms  busy {w.BusyErrors}  rows {w.RowsWritten}");
        }
        if (only == "all" || only == "lookup")
        {
            var p = Scenarios.Lookup(indexed: false, scratch);
            results.Add(p);
            Console.WriteLine($"{p.Scenario,-18} {p.Variant,-42} median {p.MedianMs,10:F1} ms  busy {p.BusyErrors}  rows {p.RowsWritten}");
            var x = Scenarios.Lookup(indexed: true, scratch);
            results.Add(x);
            Console.WriteLine($"{x.Scenario,-18} {x.Variant,-42} median {x.MedianMs,10:F1} ms  busy {x.BusyErrors}  rows {x.RowsWritten}");
        }

        Directory.CreateDirectory(outDir);
        File.WriteAllText(Path.Combine(outDir, "results.csv"),
            "scenario,variant,median_ms,runs_ms,busy_errors,rows_written,notes\n" +
            string.Join("\n", results.Select(r =>
                $"{r.Scenario},{Csv(r.Variant)},{r.MedianMs:F1},\"{string.Join(";", r.RunsMs.Select(x => x.ToString("F1")))}\",{r.BusyErrors},{r.RowsWritten},{Csv(r.Notes)}")));
        File.WriteAllText(Path.Combine(outDir, "report.md"), Report(results));
        Console.WriteLine();
        Console.WriteLine($"Wrote {Path.Combine(outDir, "report.md")} and results.csv");
        return 0;
    }

    private static string Csv(string s) => s.Contains(',') ? $"\"{s}\"" : s;

    private static string Report(List<Result> results)
    {
        var now = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss 'UTC'");
        var lines = new List<string>
        {
            "# Ombi SQLite benchmark results",
            "",
            $"Run: {now}. Host: {RuntimeInformation.OSDescription}, {RuntimeInformation.ProcessArchitecture}, " +
            $".NET {Environment.Version}. Iterations: {Iterations} per scenario (warmup discarded, median reported).",
            "",
            "Pattern scenarios (availability, whole-graph-update, refresh-metadata, lookup) run under the " +
            "current baseline SQLite configuration (DELETE journal, busy_timeout 0) so the pattern effect " +
            "is measured separately from the configuration effect. Only `contention` compares configurations.",
            "",
            "Absolute milliseconds on this host are inflated by slow fsync (each SQLite transaction " +
            "commit costs ~80 ms on this VM's disk); the ratios between variants and the busy counts " +
            "are the signal, not the absolute numbers. `Rows written` is the sum of EF Core " +
            "SaveChanges return values: for whole-graph-update it shows how many rows a one-field " +
            "change rewrites.",
            "",
            "| Scenario | Variant | Median (ms) | Runs (ms) | SQLITE_BUSY | Rows written | Notes |",
            "|---|---|---|---|---|---|---|",
        };
        lines.AddRange(results.Select(r =>
            $"| {r.Scenario} | {r.Variant} | {r.MedianMs:F1} | {string.Join(", ", r.RunsMs.Select(x => x.ToString("F1")))} | {r.BusyErrors} | {r.RowsWritten} | {r.Notes} |"));
        lines.Add("");
        lines.Add("## Reading the pairs");
        lines.Add("");
        foreach (var pair in new[]
        {
            ("availability", "Per-item SaveChanges is the current PlexAvailabilityChecker shape; the batched variant is the Emby/Jellyfin shape (mutate in loop, one save at end)."),
            ("whole-graph-update", "Db.Update on a loaded TV graph is what TvRequestRepository.Update does today; the stub variant marks only the changed property, as MarkAsAvailable already does in-repo."),
            ("refresh-metadata", "Per-item SaveChanges is the current RefreshMetadata shape on the External DB; the batched variant saves once per media type."),
            ("contention", "Same parallel-writer workload under the current config vs WAL + busy_timeout 5000. The busy count, not just the time, is the headline: a busy error in production is a failed save or a swallowed update (cf. PR #5458)."),
            ("lookup", "Point lookups by TheMovieDbId over 20,000 content rows, with and without an index on the lookup key. Current snapshots do not index these keys."),
        })
        {
            var a = results.FirstOrDefault(r => r.Scenario == pair.Item1);
            var b = results.LastOrDefault(r => r.Scenario == pair.Item1);
            if (a != null && b != null && !ReferenceEquals(a, b))
                lines.Add($"- **{pair.Item1}**: {a.Variant} {a.MedianMs:F1} ms, {a.RowsWritten} rows -> " +
                          $"{b.Variant} {b.MedianMs:F1} ms, {b.RowsWritten} rows " +
                          $"({(a.MedianMs / Math.Max(b.MedianMs, 0.001)):F1}x). {pair.Item2}");
            else
                lines.Add($"- **{pair.Item1}**: {pair.Item2}");
        }
        lines.Add("");
        lines.Add("Reproduce: `dotnet run -c Release --project benchmarks/Ombi.SqliteBenchmarks` from the repo root.");
        return string.Join("\n", lines);
    }
}
