using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;

namespace TeamNexus.Modules.Board.Endpoints;

/// <summary>
/// Maps all Board module endpoint groups (Phase 2 §2.2–§2.5). Called from Program.cs
/// after UseAuthentication/UseAuthorization. SignalR hub mapping arrives in §3.
/// </summary>
public static class BoardEndpoints
{
    public static IEndpointRouteBuilder MapBoardModuleEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapBoardsEndpoints();
        endpoints.MapColumnsEndpoints();
        endpoints.MapTasksEndpoints();
        endpoints.MapLabelsEndpoints();
        endpoints.MapCommentsEndpoints();

        return endpoints;
    }
}
