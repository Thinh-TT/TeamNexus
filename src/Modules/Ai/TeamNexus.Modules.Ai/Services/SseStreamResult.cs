using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace TeamNexus.Modules.Ai.Services;

/// <summary>
/// Writes an <see cref="IAsyncEnumerable{T}"/> of already-rendered SSE frames to the response
/// (Phase 14 §2.2).
/// <para>
/// <b>Why a hand-written <see cref="IResult"/> and not <c>Results.ServerSentEvents</c>:</b> the
/// framework helper binds the payload to a specific JSON shape, while this contract's frames are built
/// by the pure <see cref="AiChatSseWriter"/> so the exact bytes are pinned by unit tests. Writing the
/// frames straight to the body also keeps the one property that matters: text reaches the client as it
/// is produced instead of being buffered.
/// </para>
/// </summary>
internal sealed class SseStreamResult : IResult
{
    private readonly IAsyncEnumerable<string> _frames;
    private readonly ILogger _logger;

    public SseStreamResult(IAsyncEnumerable<string> frames, ILogger logger)
    {
        _frames = frames;
        _logger = logger;
    }

    public async Task ExecuteAsync(HttpContext httpContext)
    {
        httpContext.Response.StatusCode = StatusCodes.Status200OK;
        httpContext.Response.ContentType = AiChatSseWriter.ContentType;

        // Proxies (Render's edge in particular) happily buffer a "normal" response; both headers tell
        // them not to, and neither affects a direct connection.
        httpContext.Response.Headers.CacheControl = "no-cache";
        httpContext.Response.Headers["X-Accel-Buffering"] = "no";

        var body = httpContext.Response.Body;
        var ct = httpContext.RequestAborted;

        await using var enumerator = _frames.GetAsyncEnumerator(ct);

        while (true)
        {
            string frame;

            try
            {
                if (!await enumerator.MoveNextAsync())
                {
                    break;
                }

                frame = enumerator.Current;
            }
            catch (OperationCanceledException)
            {
                // The user closed the tab or pressed "Dừng". Nothing to report: the answer simply stops,
                // and logging this as a failure would fill the log with noise from normal behaviour.
                return;
            }
            catch (Exception ex)
            {
                // The headers are already sent, so the status code cannot change any more. The only
                // honest thing left is a terminal `error` frame the client already knows how to render.
                _logger.LogError(ex, "AI chat stream failed after the response had started.");

                await WriteAsync(
                    body,
                    AiChatSseWriter.Error(ex.Message),
                    CancellationToken.None);

                return;
            }

            if (frame.Length == 0)
            {
                continue;
            }

            await WriteAsync(body, frame, ct);

            // Flush explicitly: without it the answer arrives in one lump when the response completes,
            // which would make the whole streaming contract cosmetic.
            await body.FlushAsync(ct);
        }
    }

    private static async Task WriteAsync(Stream body, string frame, CancellationToken ct)
    {
        var bytes = Encoding.UTF8.GetBytes(frame);
        await body.WriteAsync(bytes, ct);
    }
}
