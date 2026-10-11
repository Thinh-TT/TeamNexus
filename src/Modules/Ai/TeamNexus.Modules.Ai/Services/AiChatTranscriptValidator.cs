namespace TeamNexus.Modules.Ai.Services;

using TeamNexus.Modules.Ai.DTOs;
using TeamNexus.Modules.Board.Services;

/// <summary>
/// One transcript validator for every chat surface (Phase 14.5 §B).
/// <para>
/// <b>Why this was extracted rather than copied.</b> Phase 14 wrote these rules once, inside
/// <c>AiChatService</c>. The board chat needs the same rules against larger numbers, and a second copy
/// would mean two places to fix the day a hole is found — a cost paid every time a request arrives,
/// versus the one-off cost of a shared pure function now. The rules are pure: they read numbers, they
/// never touch a database, a clock or a provider, so they are verifiable without any of them.
/// </para>
/// <para>
/// <b>Why it takes plain numbers instead of an options object.</b> The two chat features have different
/// option records (and will keep diverging), so the shared seam is the <i>rule</i>, not the
/// configuration shape. Each caller reads its own clamped values and states them explicitly — which is
/// also what makes the guardrail auditable at the call site.
/// </para>
/// <para>
/// <b>Why it throws at all.</b> Every rule here exists to bound cost <b>before</b> the provider is
/// called, and each violation maps to a 400 with a message that names the limit and the actual value.
/// "Request too large" would leave the caller guessing which knob to turn.
/// </para>
/// </summary>
public static class AiChatTranscriptValidator
{
    /// <summary>Roles a client may send. <c>system</c> is server-owned and never accepted from outside.</summary>
    public static readonly IReadOnlyList<string> AllowedRoles = ["user", "assistant"];

    /// <summary>
    /// Validates the replayed transcript and returns it as provider messages, or throws
    /// <see cref="BadRequestException"/> (400) naming the limit that was broken.
    /// </summary>
    public static List<AiChatMessage> Validate(
        IReadOnlyList<AiChatMessageRequest>? messages,
        int maxMessages,
        int maxTotalChars,
        int maxMessageChars = int.MaxValue)
    {
        if (messages is null || messages.Count == 0)
        {
            throw new BadRequestException("Cần ít nhất một tin nhắn để hỏi AI.");
        }

        if (messages.Count > maxMessages)
        {
            throw new BadRequestException(
                $"Lịch sử hội thoại tối đa {maxMessages} tin nhắn (nhận {messages.Count}).");
        }

        var validated = new List<AiChatMessage>(messages.Count);
        var totalChars = 0;

        foreach (var message in messages)
        {
            if (message is null || string.IsNullOrWhiteSpace(message.Role)
                || !AllowedRoles.Contains(message.Role, StringComparer.OrdinalIgnoreCase))
            {
                throw new BadRequestException(
                    "Vai trò tin nhắn phải là 'user' hoặc 'assistant'.");
            }

            var content = message.Content?.Trim();
            if (string.IsNullOrEmpty(content))
            {
                throw new BadRequestException("Tin nhắn không được để trống.");
            }

            if (content.Length > maxMessageChars)
            {
                throw new BadRequestException(
                    $"Một tin nhắn tối đa {maxMessageChars} ký tự (nhận {content.Length}).");
            }

            totalChars += content.Length;
            validated.Add(new AiChatMessage(message.Role.ToLowerInvariant(), content));
        }

        if (totalChars > maxTotalChars)
        {
            throw new BadRequestException(
                $"Tổng nội dung hội thoại tối đa {maxTotalChars} ký tự (nhận {totalChars}).");
        }

        return validated;
    }
}
