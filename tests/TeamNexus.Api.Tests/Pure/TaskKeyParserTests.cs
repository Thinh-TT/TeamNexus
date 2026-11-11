using TeamNexus.Modules.Ai.Services.Integrations;

namespace TeamNexus.Api.Tests.Pure;

public sealed class TaskKeyParserTests
{
    private readonly TaskKeyParser _parser = new();

    [Theory]
    [InlineData("TN-123", 123L)]
    [InlineData("tn-42", 42L)]
    [InlineData("Tn-999", 999L)]
    [InlineData("#100", 100L)]
    [InlineData("#1", 1L)]
    [InlineData("feat(auth): support OAuth login (TN-77)", 77L)]
    [InlineData("fix: resolve crash on save #55", 55L)]
    [InlineData("feature/TN-45-kanban-drag", 45L)]
    [InlineData("fix/issue-#88-null-ref", 88L)]
    [InlineData("Sửa lỗi giao diện hiển thị TN-12 thành công", 12L)]
    public void ParseTaskNumbers_SingleKey_ExtractsExpectedNumber(string input, long expectedNumber)
    {
        var result = _parser.ParseTaskNumbers(input);
        Assert.Single(result);
        Assert.Equal(expectedNumber, result[0]);
    }

    [Fact]
    public void ParseTaskNumbers_MultipleKeysInText_ExtractsDistinctInOrder()
    {
        var input = "PR relates to TN-10, closes #20 and also mentions tn-10 and #30";
        var result = _parser.ParseTaskNumbers(input);

        Assert.Equal(3, result.Count);
        Assert.Equal([10L, 20L, 30L], result);
    }

    [Fact]
    public void ParseTaskNumbers_ExceedingMaxCount_TruncatesToLimit()
    {
        var input = "TN-1 TN-2 TN-3 TN-4 TN-5 TN-6 TN-7";
        var result = _parser.ParseTaskNumbers(input, maxCount: 3);

        Assert.Equal(3, result.Count);
        Assert.Equal([1L, 2L, 3L], result);
    }

    [Theory]
    [InlineData("BTN-123")] // Invalid prefix
    [InlineData("TASK-123")] // Unrecognized prefix
    [InlineData("ATN-5")] // Prefix preceded by letter
    [InlineData("123TN-45")] // Preceded by digit
    [InlineData("TN-0")] // 0 is invalid
    [InlineData("#0")] // 0 is invalid
    [InlineData("TN-1234567890123456")] // Too long (>10 digits)
    [InlineData("just plain text without keys")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void ParseTaskNumbers_InvalidOrMissingKeys_ReturnsEmpty(string? input)
    {
        var result = _parser.ParseTaskNumbers(input);
        Assert.Empty(result);
    }

    [Fact]
    public void ParseTaskNumbers_MultipleSources_DeduplicatesAcrossSources()
    {
        var title = "feat: add payment gateway (TN-101)";
        var branch = "feature/TN-101-stripe";
        var body = "Closes #102 and relates to TN-101";

        var result = _parser.ParseTaskNumbers([title, branch, body], maxCount: 5);

        Assert.Equal(2, result.Count);
        Assert.Equal([101L, 102L], result);
    }

    [Fact]
    public void ParseTaskNumbers_NullAndEmptyInputsInEnumerable_HandledGracefully()
    {
        var result = _parser.ParseTaskNumbers([null, "", "   ", "TN-55", null]);
        Assert.Single(result);
        Assert.Equal(55L, result[0]);
    }

    // ---- review F5: automated GitHub paths must not act on a bare #number ---------------------

    [Theory]
    [InlineData("TN-42", 42L)]
    [InlineData("tn-42", 42L)]
    [InlineData("feat/TN-42-auth", 42L)]
    [InlineData("Sửa lỗi TN-42 gây crash", 42L)]
    public void ParseTaskNumbers_RequireTnPrefix_StillAcceptsExplicitKeys(string input, long expectedNumber)
    {
        var result = _parser.ParseTaskNumbers(input, requireTnPrefix: true);

        Assert.Single(result);
        Assert.Equal(expectedNumber, result[0]);
    }

    [Theory]
    [InlineData("#42")]
    [InlineData("Fixes #42")]
    [InlineData("Closes #7, see #12")]
    [InlineData("fix/issue-#88-null-ref")]
    [InlineData("see issues/#42 and #7")]
    public void ParseTaskNumbers_RequireTnPrefix_IgnoresBareGitHubNumbers(string input)
    {
        // These are GitHub cross-references, not TeamNexus task keys. Acting on them would move or comment
        // on an unrelated card whose task_number happens to match.
        Assert.Empty(_parser.ParseTaskNumbers(input, requireTnPrefix: true));
    }

    [Fact]
    public void ParseTaskNumbers_RequireTnPrefix_KeepsOnlyTheTnKeysInAMixedText()
    {
        var result = _parser.ParseTaskNumbers("Closes #20, relates to TN-10 and #30", requireTnPrefix: true);

        Assert.Equal([10L], result);
    }

    [Fact]
    public void ParseTaskNumbers_RequireTnPrefix_AppliesAcrossMultipleSources()
    {
        var result = _parser.ParseTaskNumbers(
            ["Fixes #7", "fix/issue-#7", "relates to TN-7"],
            requireTnPrefix: true);

        Assert.Equal([7L], result);
    }
}
