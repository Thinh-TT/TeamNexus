using System.Net;
using System.Net.Sockets;
using TeamNexus.Modules.Board.Services;

namespace TeamNexus.Modules.Ai.Services.Integrations;

/// <summary>
/// Strict SSRF guard and URL normalizer for outbound Slack and Discord incoming webhooks (Phase 15.2 §T15.2.2).
/// </summary>
public static class WebhookUrlValidator
{
    public const string ProviderSlack = "slack";
    public const string ProviderDiscord = "discord";
    public const int MaxUrlLength = 1024;

    private static readonly HashSet<string> AllowedDiscordHosts = new(StringComparer.OrdinalIgnoreCase)
    {
        "discord.com",
        "discordapp.com",
        "ptb.discord.com",
        "canary.discord.com",
        "ptb.discordapp.com",
        "canary.discordapp.com",
    };

    /// <summary>
    /// Normalizes and validates the provider and target webhook URL. Throws <see cref="BadRequestException"/>
    /// with a clear Vietnamese message when any SSRF or provider-specific rule is violated.
    /// </summary>
    public static (string NormalizedProvider, string NormalizedUrl) Validate(string? provider, string? rawUrl)
    {
        var normalizedProvider = NormalizeProvider(provider);

        if (string.IsNullOrWhiteSpace(rawUrl))
        {
            throw new BadRequestException("URL Webhook không được để trống.");
        }

        var trimmedUrl = rawUrl.Trim();
        if (trimmedUrl.Length > MaxUrlLength)
        {
            throw new BadRequestException($"URL Webhook không được vượt quá {MaxUrlLength} ký tự.");
        }

        if (!Uri.TryCreate(trimmedUrl, UriKind.Absolute, out var uri))
        {
            throw new BadRequestException("URL Webhook không đúng định dạng URI hợp lệ.");
        }

        if (!string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            throw new BadRequestException("URL Webhook bắt buộc phải sử dụng giao thức bảo mật https://.");
        }

        if (!string.IsNullOrEmpty(uri.UserInfo))
        {
            throw new BadRequestException("URL Webhook không được chứa thông tin xác thực (user:pass@).");
        }

        if (!uri.IsDefaultPort)
        {
            throw new BadRequestException("URL Webhook chỉ được phép sử dụng cổng HTTPS tiêu chuẩn (443).");
        }

        var host = uri.DnsSafeHost;
        if (IsBlockedHostOrIp(host))
        {
            throw new BadRequestException(
                "URL Webhook trỏ tới địa chỉ nội bộ hoặc IP bị cấm (chống tấn công SSRF).");
        }

        if (normalizedProvider == ProviderSlack)
        {
            ValidateSlackUri(uri, host);
        }
        else
        {
            ValidateDiscordUri(uri, host);
        }

        return (normalizedProvider, uri.AbsoluteUri);
    }

    public static string NormalizeProvider(string? provider)
    {
        var value = provider?.Trim().ToLowerInvariant();
        return value switch
        {
            ProviderSlack => ProviderSlack,
            ProviderDiscord => ProviderDiscord,
            _ => throw new BadRequestException("Nhà cung cấp Webhook không hợp lệ. Chỉ hỗ trợ 'slack' hoặc 'discord'."),
        };
    }

    /// <summary>
    /// Checks whether a host string is an IP literal, localhost, loopback, link-local, or private RFC1918 address.
    /// </summary>
    public static bool IsBlockedHostOrIp(string? host)
    {
        if (string.IsNullOrWhiteSpace(host))
        {
            return true;
        }

        var normalized = host.Trim().Trim('[', ']').ToLowerInvariant();
        if (normalized == "localhost"
            || normalized.EndsWith(".localhost", StringComparison.Ordinal)
            || normalized.EndsWith(".local", StringComparison.Ordinal)
            || normalized.EndsWith(".internal", StringComparison.Ordinal))
        {
            return true;
        }

        if (IPAddress.TryParse(normalized, out var ip))
        {
            // All raw IP literals are forbidden for webhook endpoints (private/reserved or public).
            _ = IsPrivateOrReservedIp(ip);
            return true;
        }

        return false;
    }

    /// <summary>
    /// Inspects an <see cref="IPAddress"/> for loopback (`127.0.0.0/8`, `::1`), link-local (`169.254.0.0/16`, `fe80::/10`),
    /// unspecified (`0.0.0.0`, `::`), or RFC1918 private ranges (`10.0.0.0/8`, `172.16.0.0/12`, `192.168.0.0/16`).
    /// </summary>
    public static bool IsPrivateOrReservedIp(IPAddress ip)
    {
        if (IPAddress.IsLoopback(ip) || ip.Equals(IPAddress.Any) || ip.Equals(IPAddress.IPv6Any))
        {
            return true;
        }

        if (ip.IsIPv4MappedToIPv6)
        {
            ip = ip.MapToIPv4();
        }

        if (ip.AddressFamily == AddressFamily.InterNetworkV6)
        {
            if (ip.IsIPv6LinkLocal || ip.IsIPv6SiteLocal || ip.IsIPv6Multicast)
            {
                return true;
            }

            var bytes6 = ip.GetAddressBytes();
            // Unique local IPv6 (fc00::/7)
            return (bytes6[0] & 0xFE) == 0xFC;
        }

        var bytes = ip.GetAddressBytes();
        return bytes[0] switch
        {
            0 => true,                                           // 0.0.0.0/8
            10 => true,                                          // 10.0.0.0/8 (RFC1918)
            100 when bytes[1] >= 64 && bytes[1] <= 127 => true,  // 100.64.0.0/10 (CGNAT)
            127 => true,                                         // 127.0.0.0/8 (Loopback)
            169 when bytes[1] == 254 => true,                    // 169.254.0.0/16 (Link-local / Cloud metadata)
            172 when bytes[1] >= 16 && bytes[1] <= 31 => true,   // 172.16.0.0/12 (RFC1918)
            192 when bytes[1] == 168 => true,                    // 192.168.0.0/16 (RFC1918)
            _ => false,
        };
    }

    /// <summary>
    /// Masks the secret tail of a webhook URL so it can be displayed safely in management UI responses.
    /// Example: <c>https://hooks.slack.com/services/T000/B000/****abcd</c>.
    /// </summary>
    public static string MaskUrl(string? rawUrl)
    {
        if (string.IsNullOrWhiteSpace(rawUrl))
        {
            return string.Empty;
        }

        if (!Uri.TryCreate(rawUrl.Trim(), UriKind.Absolute, out var uri))
        {
            return "****";
        }

        var path = uri.AbsolutePath.TrimEnd('/');
        var lastSlash = path.LastIndexOf('/');
        if (lastSlash < 0 || lastSlash == path.Length - 1)
        {
            return $"{uri.Scheme}://{uri.Authority}/****";
        }

        var prefix = path[..(lastSlash + 1)];
        var secretSegment = path[(lastSlash + 1)..];
        var tail = secretSegment.Length > 4
            ? secretSegment[^4..]
            : string.Empty;

        return $"{uri.Scheme}://{uri.Authority}{prefix}****{tail}";
    }

    private static void ValidateSlackUri(Uri uri, string host)
    {
        if (!string.Equals(host, "hooks.slack.com", StringComparison.OrdinalIgnoreCase))
        {
            throw new BadRequestException("Slack Webhook chỉ chấp nhận tên miền 'hooks.slack.com'.");
        }

        var path = uri.AbsolutePath;
        if (!path.StartsWith("/services/", StringComparison.Ordinal))
        {
            throw new BadRequestException("Đường dẫn Slack Webhook phải bắt đầu bằng '/services/'.");
        }

        var segments = path["/services/".Length..]
            .Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (segments.Length < 3)
        {
            throw new BadRequestException(
                "Đường dẫn Slack Webhook không hợp lệ (cần định dạng /services/{T}/{B}/{token}).");
        }
    }

    private static void ValidateDiscordUri(Uri uri, string host)
    {
        if (!AllowedDiscordHosts.Contains(host))
        {
            throw new BadRequestException(
                "Discord Webhook chỉ chấp nhận tên miền 'discord.com' hoặc 'discordapp.com'.");
        }

        var path = uri.AbsolutePath;
        if (!path.StartsWith("/api/webhooks/", StringComparison.Ordinal))
        {
            throw new BadRequestException("Đường dẫn Discord Webhook phải bắt đầu bằng '/api/webhooks/'.");
        }

        var segments = path["/api/webhooks/".Length..]
            .Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (segments.Length < 2)
        {
            throw new BadRequestException(
                "Đường dẫn Discord Webhook không hợp lệ (cần định dạng /api/webhooks/{id}/{token}).");
        }
    }
}
