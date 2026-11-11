using System.Globalization;
using System.Text.RegularExpressions;

namespace TeamNexus.Modules.Ai.Services.Integrations;

/// <summary>
/// Extracts task numbers (e.g. <c>TN-123</c>, <c>tn-123</c>, <c>#123</c>) from PR titles, branch names,
/// and descriptions (Phase 15.3 §T15.3.1).
/// <para>
/// <b>Why the <c>requireTnPrefix</c> switch exists:</b> inside GitHub content, <c>#123</c> is how GitHub
/// itself references an issue or pull request in the same repository. Auto-linking on a bare
/// <c>#number</c> therefore lets an unrelated GitHub issue number move a TeamNexus task, because the two
/// numbering spaces have nothing in common. Automated webhook paths must pass
/// <c>requireTnPrefix: true</c>; <c>#123</c> stays supported for explicit/user-facing parsing where the
/// author really means the task key.
/// </para>
/// </summary>
public partial class TaskKeyParser
{
    public const int DefaultMaxKeys = 5;

    [GeneratedRegex(@"(?<![A-Za-z0-9])(?:TN-|#)(\d{1,10})(?!\d)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex TaskKeyRegex();

    /// <summary>Unambiguous task keys only — what an automated GitHub webhook may act on.</summary>
    [GeneratedRegex(@"(?<![A-Za-z0-9])TN-(\d{1,10})(?!\d)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex TaskKeyWithPrefixRegex();

    /// <summary>
    /// Parses unique sequential task numbers from a single string.
    /// </summary>
    /// <param name="text">Free-form text (title, branch name, description, comment).</param>
    /// <param name="maxCount">Upper bound of keys returned, to keep one event from touching many tasks.</param>
    /// <param name="requireTnPrefix">
    /// When <c>true</c>, a bare <c>#123</c> is ignored. Use it on every automated GitHub path.
    /// </param>
    public IReadOnlyList<long> ParseTaskNumbers(string? text, int maxCount = DefaultMaxKeys, bool requireTnPrefix = false)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return [];
        }

        var effectiveMax = Math.Max(1, maxCount);
        var matches = (requireTnPrefix ? TaskKeyWithPrefixRegex() : TaskKeyRegex()).Matches(text);
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
    public IReadOnlyList<long> ParseTaskNumbers(
        IEnumerable<string?> texts, int maxCount = DefaultMaxKeys, bool requireTnPrefix = false)
    {
        var effectiveMax = Math.Max(1, maxCount);
        var regex = requireTnPrefix ? TaskKeyWithPrefixRegex() : TaskKeyRegex();
        var results = new List<long>();
        var seen = new HashSet<long>();

        foreach (var text in texts)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                continue;
            }

            var matches = regex.Matches(text);
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
