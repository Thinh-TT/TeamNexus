using System.Globalization;
using System.Text;

namespace TeamNexus.Modules.Reporting.Contracts;

/// <summary>
/// Dựng tên file báo cáo **ASCII-only** (Phase 6 §4.2). Hàm thuần, không I/O — verify không cần renderer.
/// <para>
/// Vì sao phải ASCII: tên file đi thẳng vào header <c>Content-Disposition</c>; tên có dấu tiếng Việt hoặc
/// ký tự điều khiển sẽ làm header lỗi/khó parse ở một số client. Nội dung báo cáo **vẫn** giữ tiếng Việt
/// đầy đủ — chỉ tên file bị chuyển thành slug.
/// </para>
/// <para>
/// Định dạng: <c>teamnexus-report-&lt;slug&gt;-&lt;yyyyMMdd-HHmm&gt;.&lt;ext&gt;</c> (mốc thời gian theo UTC).
/// </para>
/// </summary>
public static class ReportFileName
{
    /// <summary>Tiền tố cố định để file tải về dễ nhận biết.</summary>
    public const string Prefix = "teamnexus-report";

    /// <summary>Slug dùng khi tên đầu vào rỗng hoặc không còn ký tự dùng được.</summary>
    public const string FallbackSlug = "workspace";

    /// <summary>Độ dài tối đa của phần slug.</summary>
    public const int MaxSlugLength = 40;

    /// <summary>
    /// Tên file hoàn chỉnh. <paramref name="extension"/> nhận cả <c>"pdf"</c> và <c>".pdf"</c>;
    /// giá trị rỗng/ký tự lạ ⇒ <c>"bin"</c> (không bao giờ tạo tên file rỗng).
    /// </summary>
    public static string Build(string? scopeLabel, string? extension, DateTimeOffset generatedAt)
        => $"{Prefix}-{Slugify(scopeLabel)}-{generatedAt.UtcDateTime.ToString("yyyyMMdd-HHmm", CultureInfo.InvariantCulture)}.{NormalizeExtension(extension)}";

    /// <summary>
    /// Chuẩn hoá nhãn phạm vi (tên workspace/board) thành slug <c>[a-z0-9-]</c>:
    /// bỏ dấu tiếng Việt (<c>đ/Đ</c> xử lý trước vì NFD không tách được), NFD + bỏ combining marks,
    /// lowercase invariant, mọi ký tự khác thành <c>-</c>, gộp <c>-</c>, cắt hai đầu và giới hạn độ dài.
    /// </summary>
    public static string Slugify(string? scopeLabel)
    {
        var input = scopeLabel?.Trim();
        if (string.IsNullOrEmpty(input))
        {
            return FallbackSlug;
        }

        var builder = new StringBuilder(input.Length + 8);

        // `đ/Đ` không phải ký tự có dấu tổ hợp ⇒ phải map tay trước khi NFD.
        foreach (var ch in input)
        {
            builder.Append(ch switch
            {
                'đ' => 'd',
                'Đ' => 'D',
                _ => ch,
            });
        }

        var normalized = NormalizeFormD(builder.ToString());

        var slug = new StringBuilder(normalized.Length);
        foreach (var ch in normalized)
        {
            // Bỏ combining diacritical marks (U+0300..U+036F) sau NFD.
            if (ch is >= '\u0300' and <= '\u036F')
            {
                continue;
            }

            var lower = char.ToLowerInvariant(ch);
            slug.Append(lower is >= 'a' and <= 'z' or >= '0' and <= '9' ? lower : '-');
        }

        var collapsed = CollapseHyphens(slug.ToString()).Trim('-');

        if (collapsed.Length == 0)
        {
            return FallbackSlug;
        }

        return collapsed.Length <= MaxSlugLength ? collapsed : collapsed[..MaxSlugLength].Trim('-');
    }

    private static string NormalizeExtension(string? extension)
    {
        var value = extension?.Trim().TrimStart('.').ToLowerInvariant();

        return string.IsNullOrEmpty(value) || !value.All(c => c is >= 'a' and <= 'z' or >= '0' and <= '9')
            ? "bin"
            : value;
    }

    /// <summary>
    /// <see cref="string.Normalize(NormalizationForm)"/> cần globalization dữ liệu; nếu runtime bị cấu hình
    /// invariant (hoặc môi trường lạ) thì lùi về bảng map tiếng Việt để **không bao giờ** ném vì tên file.
    /// </summary>
    private static string NormalizeFormD(string value)
    {
        try
        {
            return value.Normalize(NormalizationForm.FormD);
        }
        catch (ArgumentException)
        {
            return MapVietnameseDiacritics(value);
        }
        catch (NotSupportedException)
        {
            return MapVietnameseDiacritics(value);
        }
    }

    private static string CollapseHyphens(string value)
    {
        var builder = new StringBuilder(value.Length);
        var previousHyphen = false;

        foreach (var ch in value)
        {
            var isHyphen = ch == '-';
            if (isHyphen && previousHyphen)
            {
                continue;
            }

            builder.Append(ch);
            previousHyphen = isHyphen;
        }

        return builder.ToString();
    }

    private static readonly Dictionary<char, string> VietnameseFallbackMap = new()
    {
        ['a'] = "a", ['á'] = "a", ['à'] = "a", ['ả'] = "a", ['ã'] = "a", ['ạ'] = "a",
        ['ă'] = "a", ['ắ'] = "a", ['ằ'] = "a", ['ẳ'] = "a", ['ẵ'] = "a", ['ặ'] = "a",
        ['â'] = "a", ['ấ'] = "a", ['ầ'] = "a", ['ẩ'] = "a", ['ẫ'] = "a", ['ậ'] = "a",
        ['e'] = "e", ['é'] = "e", ['è'] = "e", ['ẻ'] = "e", ['ẽ'] = "e", ['ẹ'] = "e",
        ['ê'] = "e", ['ế'] = "e", ['ề'] = "e", ['ể'] = "e", ['ễ'] = "e", ['ệ'] = "e",
        ['i'] = "i", ['í'] = "i", ['ì'] = "i", ['ỉ'] = "i", ['ĩ'] = "i", ['ị'] = "i",
        ['o'] = "o", ['ó'] = "o", ['ò'] = "o", ['ỏ'] = "o", ['õ'] = "o", ['ọ'] = "o",
        ['ô'] = "o", ['ố'] = "o", ['ồ'] = "o", ['ổ'] = "o", ['ỗ'] = "o", ['ộ'] = "o",
        ['ơ'] = "o", ['ớ'] = "o", ['ờ'] = "o", ['ở'] = "o", ['ỡ'] = "o", ['ợ'] = "o",
        ['u'] = "u", ['ú'] = "u", ['ù'] = "u", ['ủ'] = "u", ['ũ'] = "u", ['ụ'] = "u",
        ['ư'] = "u", ['ứ'] = "u", ['ừ'] = "u", ['ử'] = "u", ['ữ'] = "u", ['ự'] = "u",
        ['y'] = "y", ['ý'] = "y", ['ỳ'] = "y", ['ỷ'] = "y", ['ỹ'] = "y", ['ỵ'] = "y",
    };

    private static string MapVietnameseDiacritics(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var ch in value)
        {
            if (VietnameseFallbackMap.TryGetValue(char.ToLowerInvariant(ch), out var mapped))
            {
                builder.Append(char.IsUpper(ch) ? char.ToUpperInvariant(mapped[0]) : mapped[0]);
            }
            else
            {
                builder.Append(ch);
            }
        }

        return builder.ToString();
    }
}
