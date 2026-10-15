using TeamNexus.Modules.Ai.Services.Jev;

namespace TeamNexus.Api.Tests.Infrastructure;

/// <summary>
/// An <see cref="IJevDecisionProvider"/> that records what it was asked and answers with a fixed noul verdict
/// (Giai đoạn 14.7 Track B §B2).
/// <para>
/// <b>Why the tool suite needs it.</b> <c>ValidateAcceptanceCriteria</c> truncates the draft to 6 000
/// characters <i>before</i> building the request, and the tool's own result is a fixed two-line answer — so the
/// truncation is invisible unless the request itself is observable. The offline
/// <c>FakeJevDecisionProvider</c> answers correctly but keeps no record, and no assertion written against its
/// output could distinguish "truncated" from "sent whole".
/// </para>
/// <para>
/// It is deliberately <b>not</b> a scripted-provider clone: only the one method the tool calls is exercised, and
/// every other method would have to go through <c>JevDecisionService</c>'s own degradation path anyway.
/// </para>
/// </summary>
public sealed class RecordingJevDecisionProvider : IJevDecisionProvider
{
    private readonly object _gate = new();
    private JevDecisionRequest? _lastRequest;

    /// <summary>How many times the provider was asked. Zero proves a guard short-circuited before the call.</summary>
    public int CallCount { get; private set; }

    /// <summary>The most recent request, or <c>null</c> when nothing was asked.</summary>
    public JevDecisionRequest? LastRequest
    {
        get
        {
            lock (_gate)
            {
                return _lastRequest;
            }
        }
    }

    /// <summary>The probability of "yes" the fake verdict carries. Above 0.5 ⇒ <c>verified = true</c>.</summary>
    public double NoulProbability { get; init; } = 0.93;

    public Task<JevDecisionResponse> DecideAsync(JevDecisionRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        lock (_gate)
        {
            CallCount++;
            _lastRequest = request;
        }

        var answers = request.Questions.ToDictionary(
            question => question.Id,
            question => new JevAnswer(
                JevQuestionType.Noul,
                Noul: NoulProbability,
                Choice: null,
                Score: null,
                Probabilities: new Dictionary<string, double>(StringComparer.Ordinal),
                Confidence: null,
                Legend: new Dictionary<string, string>(StringComparer.Ordinal)),
            StringComparer.Ordinal);

        return Task.FromResult(new JevDecisionResponse(
            answers,
            "recording-jev",
            nameof(RecordingJevDecisionProvider),
            new JevUsage(InputTokens: 1, OutputTokens: 1, CostUsd: 0d)));
    }
}
