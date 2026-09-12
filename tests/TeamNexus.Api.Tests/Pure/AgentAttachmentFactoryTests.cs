using System.Text.Json;
using TeamNexus.Modules.Ai.Options;
using TeamNexus.Modules.Ai.Services.Agent;

namespace TeamNexus.Api.Tests.Pure;

/// <summary>
/// Phase 8 §2.3 — output shaping of the AI Agent Executor: Comment vs Attachment,
/// ASCII-safe file names, content-type hardening and the capped <c>tool_call_trace</c>.
/// All pure (Phase 7 D19), so this suite needs neither a database nor an AI provider.
/// </summary>
public sealed class AgentAttachmentFactoryTests
{
    private static AgentEffectiveOptions Defaults => new AgentOptions().Effective;

    // ---- ChooseKind (D9) ----------------------------------------------------

    [Fact]
    public void ChooseKind_ForShortContentWithoutFileName_IsAComment()
    {
        var kind = AgentAttachmentFactory.ChooseKind(
            content: "Tóm tắt ngắn.",
            fileName: null,
            contentType: null,
            thresholdChars: Defaults.AttachmentThresholdChars);

        Assert.Equal(AgentOutputKinds.Comment, kind);
    }

    [Fact]
    public void ChooseKind_AtExactlyTheThreshold_IsStillAComment()
    {
        var threshold = Defaults.AttachmentThresholdChars;

        var kind = AgentAttachmentFactory.ChooseKind(
            new string('a', threshold), null, null, threshold);

        Assert.Equal(AgentOutputKinds.Comment, kind);
    }

    [Fact]
    public void ChooseKind_OneCharacterPastTheThreshold_BecomesAnAttachment()
    {
        var threshold = Defaults.AttachmentThresholdChars;

        var kind = AgentAttachmentFactory.ChooseKind(
            new string('a', threshold + 1), null, null, threshold);

        Assert.Equal(AgentOutputKinds.Attachment, kind);
    }

    [Theory]
    [InlineData("report.md", null)]
    [InlineData(null, "text/markdown")]
    [InlineData("", "  ")]
    public void ChooseKind_WhenFileNameOrContentTypeIsUsable_IsAnAttachmentRegardlessOfLength(
        string? fileName, string? contentType)
    {
        var kind = AgentAttachmentFactory.ChooseKind("ngắn", fileName, contentType, Defaults.AttachmentThresholdChars);

        // Note: an empty/whitespace pair falls through to the length rule, so assert the reason.
        if (string.IsNullOrWhiteSpace(fileName) && string.IsNullOrWhiteSpace(contentType))
        {
            Assert.Equal(AgentOutputKinds.Comment, kind);
        }
        else
        {
            Assert.Equal(AgentOutputKinds.Attachment, kind);
        }
    }

    [Fact]
    public void ChooseKind_ForNullContentWithoutHint_IsAComment()
    {
        Assert.Equal(AgentOutputKinds.Comment, AgentAttachmentFactory.ChooseKind(null, null, null, 2000));
    }

    // ---- SafeFileName -------------------------------------------------------

    [Theory]
    [InlineData("../../etc/passwd")]
    [InlineData("..\\..\\windows\\system32\\config")]
    [InlineData("/absolute/path/report.md")]
    [InlineData("C:\\temp\\report.md")]
    public void SafeFileName_NeverLetsAPathEscape(string hostileInput)
    {
        var name = AgentAttachmentFactory.SafeFileName(hostileInput, null, Defaults.DefaultAttachmentContentType);

        Assert.DoesNotContain("..", name, StringComparison.Ordinal);
        Assert.DoesNotContain("/", name, StringComparison.Ordinal);
        Assert.DoesNotContain("\\", name, StringComparison.Ordinal);
        Assert.DoesNotContain(":", name, StringComparison.Ordinal);
    }

    [Fact]
    public void SafeFileName_StripsVietnameseDiacritics_AndKeepsAUsableExtension()
    {
        var name = AgentAttachmentFactory.SafeFileName(
            "Báo cáo tiến độ tháng 9.md", null, Defaults.DefaultAttachmentContentType);

        Assert.Equal("bao-cao-tien-do-thang-9.md", name);
    }

    [Fact]
    public void SafeFileName_FallsBackToTheDefaultName_WhenNothingUsableRemains()
    {
        Assert.Equal(
            AgentAttachmentFactory.FallbackFileName,
            AgentAttachmentFactory.SafeFileName("###", null, Defaults.DefaultAttachmentContentType));

        Assert.Equal(
            AgentAttachmentFactory.FallbackFileName,
            AgentAttachmentFactory.SafeFileName(null, null, Defaults.DefaultAttachmentContentType));
    }

    [Fact]
    public void SafeFileName_DerivesTheExtensionFromTheContentType_WhenTheNameHasNone()
    {
        Assert.Equal("bao-cao.csv", AgentAttachmentFactory.SafeFileName("bao cao", "text/csv", "text/markdown"));
        Assert.Equal("bao-cao.json", AgentAttachmentFactory.SafeFileName("bao cao", "application/json", "text/markdown"));
    }

    [Fact]
    public void SafeFileName_IgnoresACharsetParameterOnTheContentType()
    {
        var name = AgentAttachmentFactory.SafeFileName(
            "notes", "text/markdown; charset=utf-8", Defaults.DefaultAttachmentContentType);

        Assert.Equal("notes.md", name);
    }

    [Fact]
    public void SafeFileName_KeepsADotPrefixedFileFromBeingTreatedAsAnExtension()
    {
        // ".gitignore": the dot sits at index 0, which is not a separator, so it stays part of the
        // base name and the extension falls back to the content type's default.
        var name = AgentAttachmentFactory.SafeFileName(".gitignore", null, Defaults.DefaultAttachmentContentType);

        Assert.Equal("gitignore.md", name);
    }

    [Fact]
    public void SafeFileName_NeverExceedsTheDatabaseColumnLimit()
    {
        var hostileName = new string('a', 5_000) + ".md";

        var name = AgentAttachmentFactory.SafeFileName(hostileName, null, Defaults.DefaultAttachmentContentType);

        Assert.True(
            name.Length <= AgentAttachmentFactory.MaxFileNameLength,
            $"Expected <= {AgentAttachmentFactory.MaxFileNameLength} chars, got {name.Length}.");
    }

    [Fact]
    public void SafeFileName_RejectsAnImplausibleExtensionAndUsesTheContentTypeInstead()
    {
        // The slashes make "report" the last path segment, and "<script>alert(1)</script>" is not a
        // usable extension, so the content type decides: text/csv → .csv.
        var name = AgentAttachmentFactory.SafeFileName("report.<script>alert(1)</script>", "text/csv", "text/markdown");

        Assert.Equal("script.csv", name);
    }

    // ---- NormalizeContentType ----------------------------------------------

    [Theory]
    [InlineData("text/markdown", "text/markdown")]
    [InlineData("  application/pdf  ", "application/pdf")]
    [InlineData("text/plain", "text/plain")]
    public void NormalizeContentType_KeepsPlausibleMediaTypes(string input, string expected)
    {
        Assert.Equal(expected, AgentAttachmentFactory.NormalizeContentType(input, "text/markdown"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("notamediatype")]
    [InlineData("text/plain, text/html")]   // comma would corrupt a header
    [InlineData("text/plain; charset=utf-8")] // semicolon / charset parameter
    [InlineData("text/plain\nX-Evil: 1")]   // control character
    public void NormalizeContentType_ReplacesUnusableValuesWithTheDefault(string? input)
    {
        Assert.Equal("text/markdown", AgentAttachmentFactory.NormalizeContentType(input, "text/markdown"));
    }

    [Fact]
    public void NormalizeContentType_ReplacesAnOverlongValueThatWouldBreakTheColumn()
    {
        var tooLong = "text/" + new string('a', AgentAttachmentFactory.MaxContentTypeLength + 1);

        Assert.Equal("text/markdown", AgentAttachmentFactory.NormalizeContentType(tooLong, "text/markdown"));
    }

    [Fact]
    public void NormalizeContentType_FallsBackToMarkdownWhenTheConfiguredDefaultIsBlank()
    {
        Assert.Equal("text/markdown", AgentAttachmentFactory.NormalizeContentType(null, "   "));
    }

    // ---- BuildToolTraceEntry / AppendToolTrace (bounded jsonb) --------------

    [Fact]
    public void BuildToolTraceEntry_ProducesValidJsonWithEveryDocumentedField()
    {
        var at = DateTimeOffset.UtcNow;

        var json = AgentAttachmentFactory.BuildToolTraceEntry(
            "SearchSystemData", """{"query":"x"}""", "kết quả", isError: false, at, durationMs: 12, resultChars: 500);

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        Assert.Equal("SearchSystemData", root.GetProperty("name").GetString());
        Assert.Equal("""{"query":"x"}""", root.GetProperty("arguments").GetString());
        Assert.Equal("kết quả", root.GetProperty("resultSummary").GetString());
        Assert.False(root.GetProperty("isError").GetBoolean());
        Assert.Equal(12, root.GetProperty("durationMs").GetInt64());
    }

    [Fact]
    public void BuildToolTraceEntry_CapsArgumentsAndResultSummary()
    {
        var json = AgentAttachmentFactory.BuildToolTraceEntry(
            "WebSearch",
            new string('a', 1_000),
            new string('b', 1_000),
            isError: true,
            DateTimeOffset.UtcNow,
            durationMs: 1,
            resultChars: 50);

        using var document = JsonDocument.Parse(json);

        var arguments = document.RootElement.GetProperty("arguments").GetString()!;
        var result = document.RootElement.GetProperty("resultSummary").GetString()!;

        // Capped to the limit plus the single ellipsis marker — never the full 1 000 characters.
        Assert.Equal(51, arguments.Length);
        Assert.Equal(51, result.Length);
        Assert.True(document.RootElement.GetProperty("isError").GetBoolean());
    }

    [Fact]
    public void BuildToolTraceEntry_KeepsNullArgumentsAsNull()
    {
        var json = AgentAttachmentFactory.BuildToolTraceEntry(
            "DraftOutput", null, "ok", false, DateTimeOffset.UtcNow, 1, 500);

        using var document = JsonDocument.Parse(json);

        Assert.Equal(JsonValueKind.Null, document.RootElement.GetProperty("arguments").ValueKind);
    }

    [Fact]
    public void AppendToolTrace_AccumulatesEntriesInOrder()
    {
        var trace = "[]";

        trace = AgentAttachmentFactory.AppendToolTrace(trace, Entry("a"), 30, out var truncated1);
        trace = AgentAttachmentFactory.AppendToolTrace(trace, Entry("b"), 30, out var truncated2);

        Assert.False(truncated1);
        Assert.False(truncated2);
        Assert.Equal(["a", "b"], Names(trace));
    }

    [Fact]
    public void AppendToolTrace_AtTheEntryCap_DoesNotTruncateYet()
    {
        var trace = "[]";
        var maxEntries = 3;

        for (var i = 0; i < maxEntries; i++)
        {
            trace = AgentAttachmentFactory.AppendToolTrace(trace, Entry($"e{i}"), maxEntries, out var truncated);
            Assert.False(truncated);
        }

        using var document = JsonDocument.Parse(trace);
        Assert.Equal(maxEntries, document.RootElement.GetArrayLength());
    }

    [Fact]
    public void AppendToolTrace_OnePastTheCap_KeepsTheNewestAndFlagsTruncation()
    {
        var maxEntries = 3;
        var trace = "[]";

        for (var i = 0; i < maxEntries; i++)
        {
            trace = AgentAttachmentFactory.AppendToolTrace(trace, Entry($"e{i}"), maxEntries, out _);
        }

        trace = AgentAttachmentFactory.AppendToolTrace(trace, Entry("newest"), maxEntries, out var truncated);

        Assert.True(truncated);

        // The oldest entry is dropped, the newest survives: bounded row growth, useful tail kept.
        Assert.Equal(["e1", "e2", "newest"], Names(trace));
    }

    [Fact]
    public void AppendToolTrace_WhenTheExistingTraceIsMalformed_StartsOverInsteadOfThrowing()
    {
        var trace = AgentAttachmentFactory.AppendToolTrace("{not json", Entry("only"), 30, out var truncated);

        Assert.False(truncated);
        Assert.Equal(["only"], Names(trace));
    }

    [Fact]
    public void AppendToolTrace_WhenTheExistingTraceIsNotAnArray_StartsOver()
    {
        var trace = AgentAttachmentFactory.AppendToolTrace("""{"a":1}""", Entry("only"), 30, out _);

        Assert.Equal(["only"], Names(trace));
    }

    [Fact]
    public void AppendToolTrace_WithNullCurrentTrace_StartsANewArray()
    {
        Assert.Equal(["x"], Names(AgentAttachmentFactory.AppendToolTrace(null, Entry("x"), 30, out _)));
    }

    [Fact]
    public void AppendToolTrace_ClampsACapOfZeroOrLessToOne()
    {
        var trace = AgentAttachmentFactory.AppendToolTrace("[]", Entry("a"), 0, out _);
        trace = AgentAttachmentFactory.AppendToolTrace(trace, Entry("b"), 0, out var truncated);

        Assert.True(truncated);
        Assert.Equal(["b"], Names(trace));
    }

    private static string Entry(string name) => $$"""{"name":"{{name}}"}""";

    /// <summary>Extracts the <c>name</c> field of every entry — the trace is an array of objects.</summary>
    private static List<string?> Names(string trace)
    {
        using var document = JsonDocument.Parse(trace);

        return document.RootElement
            .EnumerateArray()
            .Select(element => element.GetProperty("name").GetString())
            .ToList();
    }
}
