using Microsoft.Extensions.DependencyInjection;
using TeamNexus.Modules.Board.Endpoints;
using TeamNexus.Modules.Board.Services;
using TeamNexus.Shared.Endpoints;

namespace TeamNexus.Modules.Board;

/// <summary>
/// DI registration for the Board module (modular monolith pattern, Phase 2 §2):
/// registers services used by the Kanban CRUD endpoints. Endpoint mapping lives in
/// <see cref="BoardEndpoints"/>, called from Program.cs.
/// </summary>
public static class BoardModule
{
    public static IServiceCollection AddBoardModule(this IServiceCollection services)
    {
        services.AddScoped<IWorkspaceAccess, WorkspaceAccess>();
        services.AddScoped<IBoardService, BoardService>();
        services.AddScoped<IColumnService, ColumnService>();
        services.AddScoped<ITaskService, TaskService>();
        services.AddScoped<ILabelService, LabelService>();
        services.AddScoped<ICommentService, CommentService>();

        // Endpoint filters resolved from DI (CSRF shared with Auth, domain errors).
        services.AddTransient<DomainExceptionFilter>();
        services.AddTransient<AntiforgeryValidationEndpointFilter>();

        return services;
    }
}
