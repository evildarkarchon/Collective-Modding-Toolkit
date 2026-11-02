namespace CMToolkit.Tests.Support;

/// <summary>Grouping of adjacent elements with equal keys, as Python's <c>itertools.groupby</c> does.</summary>
internal static class AdjacentGrouping
{
    public static IEnumerable<List<T>> GroupAdjacent<T>(this IEnumerable<T> items, Func<T, string> key)
    {
        List<T>? run = null;
        string? current = null;
        foreach (var item in items)
        {
            var k = key(item);
            if (run is not null && k == current)
            {
                run.Add(item);
                continue;
            }

            if (run is not null)
            {
                yield return run;
            }

            run = [item];
            current = k;
        }

        if (run is not null)
        {
            yield return run;
        }
    }
}
