using CMToolkit.ParityCoverage;

// Usage: dotnet run --project tools/CMToolkit.ParityCoverage -- [--strict] [--repo <path>]
// Exit codes: 0 passed, 1 failed, 2 bad arguments.
var strict = false;
string? repo = null;
for (var i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--strict":
            strict = true;
            break;
        case "--repo" when i + 1 < args.Length:
            repo = args[++i];
            break;
        default:
            Console.Error.WriteLine($"Unknown argument '{args[i]}'. Usage: CMToolkit.ParityCoverage [--strict] [--repo <path>]");
            return 2;
    }
}

var layout = repo is null ? RepoLayout.Find(Environment.CurrentDirectory) : RepoLayout.At(repo);
var report = CoverageCheck.Run(layout, strict);

Console.WriteLine($"Parity coverage ({(strict ? "strict" : "ratchet")}): {report.InventoryIds.Count} IDs, "
                  + $"{report.InventoryIds.Count - report.Unproven.Count} proven, {report.Covered.Count} in covered.txt, "
                  + $"{report.Unproven.Count} unproven.");
if (report.NotRatcheted.Count > 0)
{
    Console.WriteLine($"Proven but not in covered.txt ({report.NotRatcheted.Count}): {string.Join(", ", report.NotRatcheted)}");
}

if (report.Unproven.Count > 0)
{
    Console.WriteLine($"Unproven ({report.Unproven.Count}): {string.Join(", ", report.Unproven)}");
}

if (report.Passed)
{
    Console.WriteLine("OK");
    return 0;
}

Console.Error.WriteLine($"FAILED ({report.Errors.Count}):");
foreach (var error in report.Errors)
{
    Console.Error.WriteLine($"  {error}");
}

return 1;
