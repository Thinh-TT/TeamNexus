using System.Globalization;
using System.Net;

namespace TeamNexus.Api.Tests.Infrastructure;

/// <summary>
/// A minimal cookie jar in front of the in-process test server's handler.
/// <para>
/// Needed because <c>WebApplicationFactory.Server.CreateHandler()</c> and <c>CreateDefaultClient</c>
/// hand back <c>ClientHandler</c>, whose <c>CookieContainer</c>/<c>UseCookies</c> members are not
/// reachable through the public <see cref="HttpMessageHandler"/> surface. Owning the jar here keeps
/// the session semantics (set on a response → replay on the next request) explicit and per-client,
/// which is exactly what the refresh-rotation and logout suites depend on.
/// </para>
/// </summary>
internal sealed class SessionCookieHandler : DelegatingHandler
{    private readonly CookieContainer _jar;

    internal SessionCookieHandler(CookieContainer jar)
    {
        _jar = jar;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (request.RequestUri is not null)
        {
            var cookieHeader = _jar.GetCookieHeader(request.RequestUri);

            if (!string.IsNullOrEmpty(cookieHeader))
            {
                if (request.Headers.Contains("Cookie"))
                {
                    request.Headers.Remove("Cookie");
                }

                request.Headers.Add("Cookie", cookieHeader);
            }
        }

        var response = await base.SendAsync(request, cancellationToken);

        if (request.RequestUri is not null && response.Headers.TryGetValues("Set-Cookie", out var values))
        {
            foreach (var value in values)
            {
                var cookie = TryParseSetCookie(value, request.RequestUri.Host);

                if (cookie is null)
                {
                    continue;
                }

                // An expired cookie must actually leave the jar (logout relies on this).
                if (cookie.Expires != DateTime.MinValue && cookie.Expires <= DateTime.UtcNow)
                {
                    cookie.Expires = DateTime.UtcNow.AddDays(-1);
                }

                try
                {
                    // Adding an already-expired cookie removes it from the container.
                    _jar.Add(cookie);
                }
                catch (CookieException)
                {
                    // A domain/path the jar refuses must not fail the request under test.
                }
            }
        }

        return response;
    }

    /// <summary>
    /// Minimal <c>Set-Cookie</c> parser — the BCL exposes no public one for
    /// <see cref="System.Net.Cookie"/> on this runtime.
    /// </summary>
    private static Cookie? TryParseSetCookie(string header, string requestHost)
    {
        var segments = header.Split(';');
        if (segments.Length == 0)
        {
            return null;
        }

        var nameValue = segments[0];
        var equals = nameValue.IndexOf('=');
        if (equals <= 0)
        {
            return null;
        }

        var name = nameValue[..equals].Trim();
        var value = nameValue[(equals + 1)..].Trim();

        var cookie = new Cookie(name, Uri.UnescapeDataString(value), "/", requestHost);

        foreach (var segment in segments.Skip(1))
        {
            var attribute = segment.Trim();
            var separator = attribute.IndexOf('=');
            var key = separator > 0 ? attribute[..separator].Trim() : attribute;
            var attributeValue = separator > 0 ? attribute[(separator + 1)..].Trim() : string.Empty;

            switch (key.ToLowerInvariant())
            {
                case "path" when attributeValue.Length > 0:
                    cookie.Path = attributeValue;
                    break;
                case "domain" when attributeValue.Length > 0:
                    cookie.Domain = attributeValue.TrimStart('.');
                    break;
                case "expires" when DateTime.TryParse(
                    attributeValue,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal,
                    out var expires):
                    cookie.Expires = expires;
                    break;
                case "secure":
                    cookie.Secure = true;
                    break;
                case "httponly":
                    cookie.HttpOnly = true;
                    break;
            }
        }

        return cookie;
    }
}
