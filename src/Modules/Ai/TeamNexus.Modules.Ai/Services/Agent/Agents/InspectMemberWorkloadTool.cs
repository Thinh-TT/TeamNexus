using System.Text.Json;
using TeamNexus.Modules.Ai.Services;

namespace TeamNexus.Modules.Ai.Services.Agent.Agents;

/// <summary>
/// <c>InspectMemberWorkload</c> (Giai đoạn 14.7 Track B §B2): what one member is currently holding.
/// <para>
/// <b>Read-only and workspace-scoped by construction.</b> The workspace id comes from
/// <see cref="AgentToolContext"/>, never from the arguments, and the member is resolved through
/// <see cref="IMemberWorkloadReader"/> which filters on that workspace — so asking about a user in another
/// workspace returns "không phải thành viên", not their numbers.
/// </para>
/// <para>
/// <b>It shares the dispatcher's query on purpose.</b> §A4 ranks candidates with the same reader, so the
/// agent's own view of somebody's load can never disagree with the load figure the system used to propose
/// them. Two implementations would drift silently; one cannot.
/// </para>
/// </summary>
public sealed class InspectMemberWorkloadTool : IAgentTool
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly IMemberWorkloadReader _workloads;

    public InspectMemberWorkloadTool(IMemberWorkloadReader workloads)
    {
        _workloads = workloads;
    }

    public string Name => AgentToolDefinitions.InspectMemberWorkloadName;

    public async Task<string> ExecuteAsync(
        JsonElement arguments, AgentToolContext context, CancellationToken ct)
    {
        var raw = ToolArguments.GetTrimmedString(arguments, "userId");

        if (raw is null)
        {
            return AgentToolRegistry.Error("'userId' is required.");
        }

        if (!Guid.TryParse(raw, out var userId) || userId == Guid.Empty)
        {
            return AgentToolRegistry.Error("'userId' must be a valid GUID.");
        }

        var workload = await _workloads.LoadOneAsync(context.WorkspaceId, userId, ct);

        if (workload is null)
        {
            // Not an error condition for the model: it asked about someone who is not on the team, which is
            // information it needs to route around. Returned as a normal result so the loop continues.
            return JsonSerializer.Serialize(new
            {
                found = false,
                note = "Người này không phải thành viên của workspace hiện tại.",
            }, Json);
        }

        return JsonSerializer.Serialize(new
        {
            found = true,
            displayName = workload.DisplayName,
            primaryRole = workload.PrimaryRole,
            skills = workload.Skills,
            capacityWeight = workload.CapacityWeight,
            maxWipLimit = workload.MaxWipLimit,
            openTasks = workload.OpenTaskCount,
            overdueTasks = workload.OverdueCount,
            inProgressTasks = workload.InProgressCount,
            // The raw numbers, not a verdict: whether "6 open cards" is overload is a judgement the model
            // makes from maxWipLimit/capacityWeight, and baking a threshold in here would hide the inputs.
            atWipLimit = workload.MaxWipLimit > 0 && workload.InProgressCount >= workload.MaxWipLimit,
            isAiAgent = workload.MemberType == Persistence.Data.Entities.MemberType.AiAgent,
        }, Json);
    }
}
