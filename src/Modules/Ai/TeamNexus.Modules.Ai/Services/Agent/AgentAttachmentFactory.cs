using System.Text.Json;
using TeamNexus.Modules.Ai.Options;
using TeamNexus.Modules.Reporting.Contracts;

namespace TeamNexus.Modules.Ai.Services.Agent;

/// <summary>
/// Pure decision helpers for the agent's output (Phase 7 §4.8, D19): comment vs attachment, the
/// ASCII-safe file name and the tool-trace shaping. No I/O, no clock, no configuration plumbing —
/// every input is a parameter, which is what makes group B of the verification runnable without a
/// database or an AI provider.
/// </summary>
public static class AgentAttachmentFactory
{
    /// <summary>File name used when the model supplies nothing usable.</summary>
    public const string FallbackFileName = "ai-output.md";

    /// <summary>Base name used when the supplied name has no usable character.</summary>
    public const string FallbackBaseName = "ai-output";

    /// <summary>Matches the <c>task_attachments.file_name</c> column limit.</summary>
    public const int MaxFileNameLength = 255;

    /// <summary>Matches the <c>task_attachments.content_type</c> column limit.</summary>
    public const int MaxContentTypeLength = 128;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>Extensions used when the file name (or its extension) is unusable.</summary>
    private static readonly Dictionary<string, string> ExtensionByContentType = new(StringComparer.OrdinalIgnoreCase)
    {
        ["text/markdown"] = "md",
        ["text/x-markdown"] = "md",
        ["text/plain"] = "txt",
        ["text/csv"] = "csv",
        ["text/html"] = "html",
        ["text/xml"] = "xml",
        ["application/json"] = "json",
        ["application/xml"] = "xml",
        ["application/pdf"] = "pdf",
        ["application/zip"] = "zip",
        ["application/vnd.ms-excel"] = "xls",
        ["application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"] = "xlsx",
        ["application/vnd.openxmlformats-officedocument.wordprocessingml.document"] = "docx",
    };

    /// <summary>
    /// Comment vs attachment (D9): an explicit <c>fileName</c>/<c>contentType</c> from the model, or
    /// a body longer than <c>Agent:AttachmentThresholdChars</c>, means "this is a file"; everything
    /// else is a comment (which also keeps <c>ICommentService</c>'s 4 000-char cap satisfiable).
    /// </summary>
    public static string ChooseKind(
        string? content, string? fileName, string? contentType, int thresholdChars)
    {
        if (!string.IsNullOrWhiteSpace(fileName) || !string.IsNullOrWhiteSpace(contentType))
        {
            return AgentOutputKinds.Attachment;
        }

        return (content?.Length ?? 0) > thresholdChars
            ? AgentOutputKinds.Attachment
            : AgentOutputKinds.Comment;
    }

    /// <summary>
    /// ASCII-safe file name. Reuses <see cref="ReportFileName.Slugify"/> (Phase 6) instead of
    /// re-implementing the Vietnamese diacritic handling, but never lets the user-supplied path
    /// escape: directory components are stripped, so <c>..</c>, <c>/</c> and <c>\</c> cannot appear
    /// in the result. Falls back to <see cref="FallbackFileName"/> when nothing usable remains.
    /// </summary>
    public static string SafeFileName(string? fileName, string? contentType, string defaultContentType)
    {
        var rawName = LastPathSegment(fileName);

        var dotIndex = rawName.LastIndexOf('.');
        var rawBase = dotIndex > 0 ? rawName[..dotIndex] : rawName;
        var rawExtension = dotIndex > 0 ? rawName[(dotIndex + 1)..] : string.Empty;

        // Slugify() returns its own generic fallback ("workspace") for a name with no usable
        // character; for an attachment that would be misleading, so decide the base name first.
        var baseName = rawBase.Any(char.IsAsciiLetterOrDigit)
            ? ReportFileName.Slugify(rawBase)
            : FallbackBaseName;

        var extension = NormalizeExtension(rawExtension)
                        ?? ExtensionForContentType(contentType)
                        ?? ExtensionForContentType(defaultContentType)
                        ?? "md";

        var result = $"{baseName}.{extension}";
        return result.Length <= MaxFileNameLength ? result : result[..MaxFileNameLength];
    }

    /// <summary>
    /// Keeps only a plausible media type; anything with control characters, whitespace, a comma or an
    /// over-long value is replaced by <paramref name="defaultContentType"/>. Prevents a header/DB
    /// failure caused purely by a hallucinated value.
    /// </summary>
    public static string NormalizeContentType(string? contentType, string defaultContentType)
    {
        var fallback = string.IsNullOrWhiteSpace(defaultContentType)
            ? "text/markdown"
            : defaultContentType.Trim();

        var value = contentType?.Trim();
        if (string.IsNullOrEmpty(value)
            || value.Length > MaxContentTypeLength
            || !value.Contains('/')
            || value.Any(c => c < ' ' || c == ',' || c == ';' || c == ':'))
        {
            return fallback;
        }

        return value;
    }

    /// <summary>
    /// One compact <c>tool_call_trace</c> entry (jsonb): the argument/result are stored as text and
    /// the result is capped, so a run can never write an unbounded row (D19).
    /// </summary>
    public static string BuildToolTraceEntry(
        string name,
        string? arguments,
        string? result,
        bool isError,
        DateTimeOffset at,
        long durationMs,
        int resultChars)
        => JsonSerializer.Serialize(new
        {
            name,
            arguments = Cap(arguments, resultChars),
            resultSummary = Cap(result, resultChars),
            isError,
            at,
            durationMs,
        }, Json);

    /// <summary>
    /// Appends one entry to the raw trace JSON array, keeping at most <paramref name="maxEntries"/>
    /// entries. Returns the new array plus whether anything was dropped, so the caller can persist
    /// <c>trace_truncated</c>.
    /// </summary>
    public static string AppendToolTrace(
        string? currentTrace, string entryJson, int maxEntries, out bool truncated)
    {
        var entries = new List<string>();

        if (!string.IsNullOrWhiteSpace(currentTrace))
        {
            try
            {
                using var document = JsonDocument.Parse(currentTrace);
                if (document.RootElement.ValueKind == JsonValueKind.Array)
                {
                    entries.AddRange(document.RootElement.EnumerateArray().Select(e => e.GetRawText()));
                }
            }
            catch (JsonException)
            {
                // A malformed trace is dropped rather than failing the run.
                entries.Clear();
            }
        }

        entries.Add(entryJson);

        truncated = false;
        if (entries.Count > Math.Max(1, maxEntries))
        {
            entries = entries[^Math.Max(1, maxEntries)..];
            truncated = true;
        }

        return "[" + string.Join(",", entries) + "]";
    }

    private static string? NormalizeExtension(string? extension)
    {
        var value = extension?.Trim().TrimStart('.').ToLowerInvariant();

        return string.IsNullOrEmpty(value)
               || value.Length > 10
               || !value.All(c => c is >= 'a' and <= 'z' or >= '0' and <= '9')
            ? null
            : value;
    }

    private static string? ExtensionForContentType(string? contentType)
    {
        var value = contentType?.Trim();
        if (string.IsNullOrEmpty(value))
        {
            return null;
        }

        // Ignore a charset parameter: "text/markdown; charset=utf-8".
        var semicolon = value.IndexOf(';');
        if (semicolon > 0)
        {
            value = value[..semicolon].Trim();
        }

        return ExtensionByContentType.GetValueOrDefault(value);
    }

    /// <summary>Keeps only the last path segment of a user/model supplied name.</summary>
    private static string LastPathSegment(string? fileName)
    {
        var value = fileName?.Trim();
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        var slash = Math.Max(value.LastIndexOf('/'), value.LastIndexOf('\\'));
        return slash >= 0 ? value[(slash + 1)..].Trim() : value;
    }

    private static string? Cap(string? value, int limit)
    {
        if (value is null)
        {
            return null;
        }

        return value.Length <= limit ? value : value[..limit] + "…";
    }
}
