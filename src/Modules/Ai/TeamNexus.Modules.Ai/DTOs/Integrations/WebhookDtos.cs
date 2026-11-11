using System.Text.Json;
using System.Text.Json.Serialization;

namespace TeamNexus.Modules.Ai.DTOs.Integrations;

public sealed record WorkspaceWebhookStatsResponse(
    int SentLast24h,
    int FailedLast24h,
    int PendingCount);

public sealed record WorkspaceWebhookEndpointResponse(
    Guid Id,
    Guid WorkspaceId,
    string Name,
    string Provider,
    string MaskedTargetUrl,
    IReadOnlyList<string> SubscribedEvents,
    string SubscribedEventsCsv,
    bool IsActive,
    int FailureCount,
    DateTimeOffset? LastSuccessAt,
    DateTimeOffset? LastFailureAt,
    string? LastError,
    Guid CreatedByUserId,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record WorkspaceWebhookListResponse(
    WorkspaceWebhookStatsResponse Stats,
    IReadOnlyList<WorkspaceWebhookEndpointResponse> Endpoints);

public sealed record CreateWorkspaceWebhookRequest(
    string Name,
    string Provider,
    string TargetUrl,
    [property: JsonConverter(typeof(FlexibleStringListJsonConverter))]
    IReadOnlyList<string>? SubscribedEvents = null);

public sealed record UpdateWorkspaceWebhookRequest(
    string? Name = null,
    string? TargetUrl = null,
    [property: JsonConverter(typeof(FlexibleStringListJsonConverter))]
    IReadOnlyList<string>? SubscribedEvents = null,
    bool? IsActive = null);

public sealed record WebhookTestResultResponse(
    bool Success,
    Guid DeliveryId,
    string Status,
    int? HttpStatus,
    string? Error,
    DateTimeOffset TestedAt);

public sealed record WebhookDeliveryItemResponse(
    Guid Id,
    Guid EndpointId,
    string EventType,
    string Status,
    int AttemptCount,
    int MaxAttempts,
    int? LastHttpStatus,
    string? LastError,
    DateTimeOffset NextAttemptAt,
    DateTimeOffset? SentAt,
    DateTimeOffset CreatedAt);

/// <summary>
/// Allows <c>subscribedEvents</c> in request payloads to be supplied either as a JSON string array
/// (<c>["ObserverAlert", "MemberJoined"]</c>) or as a comma-separated string (<c>"ObserverAlert,MemberJoined"</c>).
/// </summary>
public sealed class FlexibleStringListJsonConverter : JsonConverter<IReadOnlyList<string>?>
{
    public override IReadOnlyList<string>? Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
        {
            return null;
        }

        if (reader.TokenType == JsonTokenType.String)
        {
            var csv = reader.GetString();
            if (string.IsNullOrWhiteSpace(csv))
            {
                return [];
            }

            return csv
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToList();
        }

        if (reader.TokenType == JsonTokenType.StartArray)
        {
            var list = new List<string>();
            while (reader.Read())
            {
                if (reader.TokenType == JsonTokenType.EndArray)
                {
                    break;
                }

                if (reader.TokenType == JsonTokenType.String && reader.GetString() is { } item)
                {
                    list.Add(item);
                }
            }

            return list;
        }

        throw new JsonException("Trường subscribedEvents phải là mảng chuỗi hoặc chuỗi phân tách bằng dấu phẩy.");
    }

    public override void Write(
        Utf8JsonWriter writer,
        IReadOnlyList<string>? value,
        JsonSerializerOptions options)
    {
        if (value is null)
        {
            writer.WriteNullValue();
            return;
        }

        writer.WriteStartArray();
        foreach (var item in value)
        {
            writer.WriteStringValue(item);
        }

        writer.WriteEndArray();
    }
}

