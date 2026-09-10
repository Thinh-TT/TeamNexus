using Microsoft.Extensions.DependencyInjection;
using TeamNexus.Modules.Board.Endpoints;
using TeamNexus.Modules.Board.Services;
using TeamNexus.Shared.Endpoints;

namespace TeamNexus.Modules.Board;

/// <summary>
/// DI registration for the Board module (modular monolith pattern, Phase 2 §2–§3):
/// Kanban CRUD services + SignalR (BoardHub) real-time broadcasting. Endpoint mapping
/// lives in <see cref="BoardEndpoints"/> (REST) and Hubs (SignalR), called from Program.cs.
/// </summary>
public static class BoardModule
{
    public static IServiceCollection AddBoardModule(this IServiceCollection services)
    {
        services.AddScoped<IWorkspaceAccess, WorkspaceAccess>();
        services.AddScoped<IWorkspaceMemberService, WorkspaceMemberService>();
        services.AddScoped<IBoardService, BoardService>();
        services.AddScoped<IColumnService, ColumnService>();
        services.AddScoped<ITaskService, TaskService>();
        services.AddScoped<ILabelService, LabelService>();
        services.AddScoped<ICommentService, CommentService>();
        services.AddScoped<IBoardEventPublisher, BoardEventPublisher>();

        // Activity log port (Phase 5 §2): Board declares it, module Ai registers the real EF
        // adapter. That registration wins because Program.cs calls AddBoardModule before
        // AddAiModule — without the no-op below the Board module alone could not resolve
        // TaskService/CommentService.
        services.AddScoped<IActivityLogWriter, NullActivityLogWriter>();

        // Real-time: BoardHub over WebSockets/SSE/long-polling (Phase 2 §3).
        services.AddSignalR();

        // Endpoint filters resolved from DI (CSRF shared with Auth, domain errors).
        services.AddTransient<DomainExceptionFilter>();
        services.AddTransient<AntiforgeryValidationEndpointFilter>();

        return services;
    }
}
