using System.Text.Json;

namespace TeamNexus.Modules.Ai.Services.Agent.Agents;

/// <summary>
/// Shared defensive readers for tool arguments (Phase 7 §4.4). The JSON comes from the model, so
/// every read is tolerant and never throws — a wrong type is treated as "absent".
/// </summary>
internal static class ToolArguments
{
    public static string? GetString(JsonElement arguments, string name)
    {
        if (arguments.ValueKind != JsonValueKind.Object || !arguments.TryGetProperty(name, out var value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.GetRawText(),
            JsonValueKind.True or JsonValueKind.False => value.GetRawText(),
            _ => null,
        };
    }

    /// <summary>Integer argument; also tolerates a numeric string ("10") which models emit often.</summary>
    public static int? GetInt(JsonElement arguments, string name)
    {
        if (arguments.ValueKind != JsonValueKind.Object || !arguments.TryGetProperty(name, out var value))
        {
            return null;
        }

        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number))
        {
            return number;
        }

        if (value.ValueKind == JsonValueKind.String
            && int.TryParse(value.GetString(), out var parsed))
        {
            return parsed;
        }

        return null;
    }

    /// <summary>Trims and treats whitespace-only as absent.</summary>
    public static string? GetTrimmedString(JsonElement arguments, string name)
    {
        var value = GetString(arguments, name)?.Trim();
        return string.IsNullOrEmpty(value) ? null : value;
    }
}
