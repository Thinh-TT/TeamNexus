using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;

namespace TeamNexus.Modules.Board.Endpoints;

/// <summary>
/// Maps all Board module endpoint groups (Phase 2 §2.2–§2.5). Called from Program.cs
/// after UseAuthentication/UseAuthorization. The SignalR hub is mapped by
/// <c>MapBoardHub</c> (Hubs/BoardHub.cs), also from Program.cs.
/// </summary>
public static class BoardEndpoints
{
    public static IEndpointRouteBuilder MapBoardModuleEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapWorkspacesEndpoints();
        endpoints.MapBoardsEndpoints();
        endpoints.MapColumnsEndpoints();
        endpoints.MapTasksEndpoints();
        endpoints.MapLabelsEndpoints();
        endpoints.MapCommentsEndpoints();
        endpoints.MapMembersEndpoints();
        endpoints.MapInvitationsEndpoints();
        endpoints.MapProfileEndpoints();

        return endpoints;
    }
}
