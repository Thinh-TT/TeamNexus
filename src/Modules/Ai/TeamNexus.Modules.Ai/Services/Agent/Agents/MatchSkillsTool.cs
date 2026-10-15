using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TeamNexus.Persistence.Data;
using TeamNexus.Persistence.Data.Entities;

namespace TeamNexus.Modules.Ai.Services.Agent.Agents;

/// <summary>
/// <c>MatchSkills</c> (Giai đoạn 14.7 Track B §B2): who in this workspace actually has the skills a task needs.
/// <para>
/// <b>No model call at all.</b> This is a set-intersection over <c>workspace_members.skills</c>, so it is
/// deterministic, free, instant, and — the reason it is built this way — fully testable offline in the pure
/// suite. Asking Jev "who knows React?" would spend tokens on a question the database can answer exactly.
/// </para>
/// <para>
/// <b>The scoring is deliberately transparent</b> (covered, missing, a simple ratio) rather than a single
/// opaque number, so the model can explain its choice and a human reading the trace can check it.
/// </para>
/// </summary>
public sealed class MatchSkillsTool : IAgentTool
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private const int DefaultLimit = 5;

    private readonly TeamNexusDbContext _db;

    public MatchSkillsTool(TeamNexusDbContext db)
    {
        _db = db;
    }

    public string Name => AgentToolDefinitions.MatchSkillsName;

    public async Task<string> ExecuteAsync(
        JsonElement arguments, AgentToolContext context, CancellationToken ct)
    {
        var requested = ReadSkills(arguments);

        if (requested.Count == 0)
        {
            return AgentToolRegistry.Error(
                "'skills' is required and must contain at least one non-empty slug.");
        }

        var limit = Math.Clamp(
            ToolArguments.GetInt(arguments, "limit") ?? DefaultLimit,
            1,
            AgentToolDefinitions.MaxSkillMatches);

        // Read every member of this workspace (membership is small by nature) and rank in memory: the skill
        // list is jsonb, so a SQL-side intersection would be provider-specific and would still have to be
        // ranked afterwards.
        var members = await _db.WorkspaceMembers
            .AsNoTracking()
            .Where(wm => wm.WorkspaceId == context.WorkspaceId)
            .Include(wm => wm.User)
            .ToListAsync(ct);

        var matches = members
            .Where(wm => wm.User is not null)
            .Select(member =>
            {
                var owned = member.Skills.ToHashSet(StringComparer.OrdinalIgnoreCase);
                var covered = requested.Where(owned.Contains).ToList();

                return new
                {
                    Member = member,
                    Covered = covered,
                    Missing = requested.Where(skill => !covered.Contains(skill)).ToList(),
                };
            })
            // A member with zero overlap is not a "match" — returning them would only add noise the model has
            // to filter out, and the whole point of this tool is to narrow the field.
            .Where(row => row.Covered.Count > 0)
            .OrderByDescending(row => row.Covered.Count)
            // Capacity breaks ties so a fully-covered overloaded member does not outrank a fully-covered
            // available one; the join date keeps the order stable across identical requests.
            .ThenByDescending(row => row.Member.CapacityWeight)
            .ThenBy(row => row.Member.JoinedAt)
            .Take(limit)
            .ToList();

        return JsonSerializer.Serialize(new
        {
            requestedSkills = requested,
            count = matches.Count,
            matches = matches.Select(row => new
            {
                userId = row.Member.UserId,
                displayName = row.Member.User!.DisplayName,
                primaryRole = row.Member.PrimaryRole,
                coveredSkills = row.Covered,
                missingSkills = row.Missing,
                // Rounding to 3 decimals: the raw double would serialise as 0.6666666666666666 and invite the
                // model to quote meaningless precision.
                matchRatio = Math.Round((double)row.Covered.Count / requested.Count, 3),
                capacityWeight = row.Member.CapacityWeight,
                maxWipLimit = row.Member.MaxWipLimit,
                isAiAgent = row.Member.MemberType == MemberType.AiAgent,
            }),
        }, Json);
    }

    /// <summary>
    /// Reads the (untrusted) <c>skills</c> argument: a JSON array of strings. Blank entries are dropped and
    /// duplicates removed case-insensitively, because the ratio reported back is computed against this list —
    /// letting "React" and "react" both count would inflate it.
    /// </summary>
    private static List<string> ReadSkills(JsonElement arguments)
    {
        if (arguments.ValueKind != JsonValueKind.Object
            || !arguments.TryGetProperty("skills", out var value))
        {
            return [];
        }

        // A model that sends a bare string instead of a one-element array is making the intent obvious; the
        // tolerant read is what keeps a run alive over a formatting slip.
        var items = value.ValueKind == JsonValueKind.String
            ? [value.GetString()]
            : value.ValueKind == JsonValueKind.Array
                ? value.EnumerateArray().Select(item => item.ValueKind == JsonValueKind.String
                    ? item.GetString()
                    : null).ToArray()
                : [];

        var result = new List<string>();

        foreach (var item in items)
        {
            var slug = item?.Trim().ToLowerInvariant();

            if (string.IsNullOrEmpty(slug) || slug.Length > AgentToolDefinitions.MaxSkillSlugLength)
            {
                continue;
            }

            if (!result.Contains(slug, StringComparer.Ordinal))
            {
                result.Add(slug);
            }
        }

        return result;
    }
}
