namespace SysMonitor.Core.Services.Utilities;

/// <summary>
/// Says what became of a batch of files handed to the Recycle Bin, in a sentence or three: how many went and
/// how much they held, how many were left where they are and why, and any that are gone without being there.
/// </summary>
public static class RecycleReport
{
    /// <param name="files">Every file handed over, with what became of it.</param>
    /// <param name="formatSize">Sizes as the page shows them elsewhere, so the numbers match.</param>
    public static string Describe(IReadOnlyCollection<RecycledFile> files, Func<long, string> formatSize)
    {
        var recycled = files.Where(file => file.Result.Outcome == RecycleOutcome.Recycled).ToList();
        var left = files.Where(file => file.Result.Outcome is RecycleOutcome.Refused or RecycleOutcome.Failed).ToList();
        var missing = files.Count(file => file.Result.Outcome == RecycleOutcome.Missing);
        var lost = files.Count(file => file.Result.Outcome == RecycleOutcome.NotInRecycleBin);

        var sentences = new List<string>
        {
            recycled.Count > 0
                ? $"Moved {Files(recycled.Count)} ({formatSize(recycled.Sum(file => file.Bytes))}) to the Recycle Bin."
                : "Nothing was moved to the Recycle Bin.",
        };

        if (left.Count > 0)
        {
            var reasons = left.GroupBy(file => file.Result.Reason).ToList();
            sentences.Add(reasons.Count == 1
                ? $"{Files(left.Count)} {reasons[0].Key} {(left.Count == 1 ? "was" : "were")} left where " +
                  $"{(left.Count == 1 ? "it is" : "they are")}."
                : $"{Files(left.Count)} were left where they are: " +
                  string.Join(", ", reasons.Select(reason => $"{reason.Count()} {reason.Key}")) + ".");
        }

        if (missing > 0)
            sentences.Add($"{Files(missing)} {(missing == 1 ? "was" : "were")} no longer there.");

        if (lost > 0)
        {
            sentences.Add($"{Files(lost)} {(lost == 1 ? "is" : "are")} gone but not in the Recycle Bin: treat " +
                          $"{(lost == 1 ? "it" : "them")} as deleted.");
        }

        return string.Join(" ", sentences);
    }

    private static string Files(int count) => count == 1 ? "1 file" : $"{count} files";
}
