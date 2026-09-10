using System.Text.Json;

namespace TeamNexus.Modules.Ai.Services;

/// <summary>
/// Builds the persisted shape of an Observer alert (Phase 5 §4.4). Pure and public so the payload
/// shape and the deduplication key can be verified without a database.
/// <para>
/// The dedupe key is <b>semantic</b>: it is derived from the alert type plus the <i>sorted</i>
/// evidence ids, never from the serialized JSON, because PostgreSQL normalizes <c>jsonb</c>
/// (key order/whitespace) and string comparison would be unreliable (Phase 4 §1.2).
/// </para>
/// </summary>
public static class ObserverNotificationFactory
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>jsonb <c>payload</c>: trace back to the run plus the evidence behind the alert.</summary>
    public static string BuildPayload(ObserverFinding finding, ObserverRunContext context, Guid? boardId = null)
        => JsonSerializer.Serialize(new
        {
            runId = context.RunId,
            type = finding.Type,
            severity = finding.Severity,
            boardId,
            taskIds = finding.TaskIds,
            userIds = finding.UserIds,
            model = context.Model,
            promptTokens = context.PromptTokens,
            completionTokens = context.CompletionTokens,
        }, Json);

    /// <summary>Alert title shown in the notification center.</summary>
    public static string BuildTitle(ObserverFinding finding) => finding.Title;

    /// <summary>Alert body shown in the notification center.</summary>
    public static string BuildMessage(ObserverFinding finding) => finding.Message;

    /// <summary>Dedupe key for a finding about to be persisted.</summary>
    public static string BuildDedupeKey(ObserverFinding finding)
        => BuildDedupeKey(finding.Type, finding.TaskIds, finding.UserIds);

    /// <summary>
    /// Dedupe key derived from an already-stored <c>payload</c> (jsonb read back from the database),
    /// tolerating missing/invalid JSON: such a row simply cannot suppress anything.
    /// </summary>
    public static string BuildDedupeKey(string type, string? payloadJson)
    {
        if (AiActionService.ParseJson(payloadJson) is not { ValueKind: JsonValueKind.Object } root)
        {
            return string.Empty;
        }

        return BuildDedupeKey(type, ReadIds(root, "taskIds"), ReadIds(root, "userIds"));
    }

    private static string BuildDedupeKey(string type, IReadOnlyList<Guid> taskIds, IReadOnlyList<Guid> userIds)
    {
        var tasks = string.Join(",", taskIds.OrderBy(id => id).Select(id => id.ToString("N")));
        var users = string.Join(",", userIds.OrderBy(id => id).Select(id => id.ToString("N")));
        return $"{type}|{tasks}|{users}";
    }

    private static IReadOnlyList<Guid> ReadIds(JsonElement parent, string propertyName)
    {
        if (!parent.TryGetProperty(propertyName, out var array) || array.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var ids = new List<Guid>();
        foreach (var item in array.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.String && Guid.TryParse(item.GetString(), out var id))
            {
                ids.Add(id);
            }
        }

        return ids;
    }
}
