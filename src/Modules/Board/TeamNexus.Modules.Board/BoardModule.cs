using Microsoft.Extensions.Configuration;
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
    public static IServiceCollection AddBoardModule(
        this IServiceCollection services,
        IConfiguration? configuration = null)
    {
        services.AddScoped<IWorkspaceAccess, WorkspaceAccess>();
        services.AddScoped<IWorkspaceMemberService, WorkspaceMemberService>();
        services.AddScoped<IWorkspaceService, WorkspaceService>();
        services.AddScoped<IWorkspaceActivityService, WorkspaceActivityService>();
        services.AddScoped<IInvitationService, InvitationService>();
        services.AddScoped<IQuickEmailService, QuickEmailService>();
        services.AddScoped<IUserProfileService, UserProfileService>();
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

        // AI Agent identity port (Phase 7 §3.4, decision D7): same trick as IActivityLogWriter —
        // Board declares the port, module Ai registers WorkspaceAiAgentResolver afterwards.
        services.AddScoped<IAiAgentResolver, NullAiAgentResolver>();

        // User-facing notification port (Phase 11 §6.3): Board declares it, module Ai registers the
        // real EF adapter afterwards. Null default keeps the Board module self-sufficient.
        services.AddScoped<INotificationWriter, NullNotificationWriter>();

        // Email port + invitation settings (Phase 11 §2, decision D9): Board declares both so the
        // invitation service can compose an accept link and ask for the email to be sent, without
        // referencing module Ai (which already references Board). Module Ai overrides both.
        services.AddScoped<IEmailGateway, NullEmailGateway>();
        services.AddScoped<WorkspaceEmailOptions>();
        services.AddScoped(provider =>
        {
            var frontend = configuration?.GetSection("Frontend:BaseUrl").Value;

            return new InvitationSettings
            {
                FrontendBaseUrl = string.IsNullOrWhiteSpace(frontend)
                    ? "http://localhost:5173"
                    : frontend,
            };
        });

        // Real-time: BoardHub over WebSockets/SSE/long-polling (Phase 2 §3).
        services.AddSignalR();

        // Endpoint filters resolved from DI (CSRF shared with Auth, domain errors).
        services.AddTransient<DomainExceptionFilter>();
        services.AddTransient<AntiforgeryValidationEndpointFilter>();

        return services;
    }
}
