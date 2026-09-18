using System.Net.Http.Json;
using TeamNexus.Modules.Auth;

namespace TeamNexus.Api.Tests.Infrastructure;

/// <summary>
/// An <see cref="HttpClient"/> for the integration suites that behaves like the SPA's
/// <c>httpClient.ts</c>: it echoes the <c>XSRF-TOKEN</c> cookie in the <c>X-XSRF-TOKEN</c> header on
/// every mutating verb.
/// <para>
/// This matters because every state-changing endpoint carries
/// <c>AntiforgeryValidationEndpointFilter</c>. A bare <see cref="HttpClient"/> gets a
/// <b>403</b> on POST/PUT/DELETE, which would drown real assertions in CSRF noise and hide the
/// behaviour actually under test.
/// </para>
/// <para>
/// Read verbs are deliberately left alone: GETs are not CSRF-protected, and polluting them with the
/// token would prove nothing.
/// </para>
/// </summary>
public sealed class TestHttpClient : IDisposable
{
    private static readonly HashSet<HttpMethod> MutatingMethods =
    [
        HttpMethod.Post, HttpMethod.Put, HttpMethod.Patch, HttpMethod.Delete,
    ];

    private readonly TestScenario _scenario;

    internal TestHttpClient(HttpClient http, TestScenario scenario)
    {
        Http = http;
        _scenario = scenario;
    }

    /// <summary>The underlying client, for cases that need raw control (e.g. asserting on a 403).</summary>
    public HttpClient Http { get; }

    // ---- reads --------------------------------------------------------------

    public Task<HttpResponseMessage> GetAsync(string url)
        => Http.GetAsync(url);

    public Task<T?> GetJsonAsync<T>(string url)
        => Http.GetFromJsonAsync<T>(url);

    // ---- writes (CSRF header attached automatically) ------------------------

    public Task<HttpResponseMessage> PostJsonAsync<TValue>(string url, TValue value)
        => SendJsonAsync(HttpMethod.Post, url, value);

    public Task<HttpResponseMessage> PutJsonAsync<TValue>(string url, TValue value)
        => SendJsonAsync(HttpMethod.Put, url, value);

    public Task<HttpResponseMessage> DeleteAsync(string url)
        => SendAsync(new HttpRequestMessage(HttpMethod.Delete, url));

    /// <summary>Sends an already-built request with the antiforgery header applied when needed.</summary>
    public Task<HttpResponseMessage> SendAsync(HttpRequestMessage request)
    {
        if (MutatingMethods.Contains(request.Method))
        {
            _scenario.ApplyAntiforgeryHeader(request);
        }

        return Http.SendAsync(request);
    }

    /// <summary>
    /// Sends a mutating request and returns as soon as the <b>response headers</b> arrive (Phase 14 §2).
    /// <para>
    /// Needed by the SSE suites: the default <see cref="HttpClient.SendAsync(HttpRequestMessage)"/>
    /// buffers the whole body before returning, so a streaming endpoint and a buffered one are
    /// indistinguishable — and the assertion that matters ("a meta frame arrives before any text") could
    /// never be made. The antiforgery header is applied exactly as in <see cref="SendAsync"/>.
    /// </para>
    /// </summary>
    public Task<HttpResponseMessage> SendStreamingAsync(HttpRequestMessage request)
    {
        if (MutatingMethods.Contains(request.Method))
        {
            _scenario.ApplyAntiforgeryHeader(request);
        }

        return Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
    }

    /// <summary>Removes the antiforgery header — used by the tests that assert on the 403 path.</summary>
    public void RemoveAntiforgeryHeader()
        => Http.DefaultRequestHeaders.Remove(AuthConstants.XsrfRequestHeader);

    public void Dispose() => Http.Dispose();

    private Task<HttpResponseMessage> SendJsonAsync<TValue>(HttpMethod method, string url, TValue value)
    {
        var request = new HttpRequestMessage(method, url)
        {
            Content = JsonContent.Create(value),
        };

        return SendAsync(request);
    }
}
