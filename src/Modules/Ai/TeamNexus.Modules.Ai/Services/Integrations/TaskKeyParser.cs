using System.Globalization;
using System.Text.RegularExpressions;

namespace TeamNexus.Modules.Ai.Services.Integrations;

/// <summary>
/// Extracts task numbers (e.g. <c>TN-123</c>, <c>tn-123</c>, <c>#123</c>) from PR titles, branch names,
/// and descriptions (Phase 15.3 §T15.3.1).
/// </summary>
public partial class TaskKeyParser
{
    public const int DefaultMaxKeys = 5;

    [GeneratedRegex(@"(?<![A-Za-z0-9])(?:TN-|#)(\d{1,10})(?!\d)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex TaskKeyRegex();

    /// <summary>
    /// Parses unique sequential task numbers from a single string.
    /// </summary>
    public IReadOnlyList<long> ParseTaskNumbers(string? text, int maxCount = DefaultMaxKeys)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return [];
        }

        var effectiveMax = Math.Max(1, maxCount);
        var matches = TaskKeyRegex().Matches(text);
        if (matches.Count == 0)
        {
            return [];
        }

        var results = new List<long>();
        var seen = new HashSet<long>();

        foreach (Match match in matches)
        {
            if (match.Success && match.Groups.Count > 1)
            {
                var digits = match.Groups[1].ValueSpan;
                if (long.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out var taskNumber)
                    && taskNumber > 0
                    && seen.Add(taskNumber))
                {
                    results.Add(taskNumber);
                    if (results.Count >= effectiveMax)
                    {
                        break;
                    }
                }
            }
        }

        return results;
    }

    /// <summary>
    /// Parses unique sequential task numbers from multiple text inputs in priority order
    /// (e.g. PR title, branch ref, and body).
    /// </summary>
    public IReadOnlyList<long> ParseTaskNumbers(IEnumerable<string?> texts, int maxCount = DefaultMaxKeys)
    {
        var effectiveMax = Math.Max(1, maxCount);
        var results = new List<long>();
        var seen = new HashSet<long>();

        foreach (var text in texts)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                continue;
            }

            var matches = TaskKeyRegex().Matches(text);
            foreach (Match match in matches)
            {
                if (match.Success && match.Groups.Count > 1)
                {
                    var digits = match.Groups[1].ValueSpan;
                    if (long.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out var taskNumber)
                        && taskNumber > 0
                        && seen.Add(taskNumber))
                    {
                        results.Add(taskNumber);
                        if (results.Count >= effectiveMax)
                        {
                            return results;
                        }
                    }
                }
            }
        }

        return results;
    }
}
