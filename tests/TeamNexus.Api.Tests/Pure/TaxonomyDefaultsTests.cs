using TeamNexus.Modules.Board.DTOs;
using TeamNexus.Modules.Board.Services;
using TeamNexus.Persistence.Data.Entities;

namespace TeamNexus.Api.Tests.Pure;

/// <summary>
/// Giai đoạn 14.7 §A2 — the pure rules behind the fixed vocabulary and the member capacity profile.
/// <para>
/// No database and no HTTP: these are the rules the integration suites rely on, and pinning them separately
/// means a failure points at the rule rather than at the plumbing.
/// </para>
/// </summary>
public sealed class TaxonomyDefaultsTests
{
    [Fact]
    public void DomainsAndWorkTypes_AreBothNonEmptyBecauseAnEmptyKindMakesJevUnanswerable()
    {
        Assert.NotEmpty(TaxonomyDefaults.Domains);
        Assert.NotEmpty(TaxonomyDefaults.WorkTypes);
    }

    [Fact]
    public void Seeds_UseUniqueKeysWithinAKind()
    {
        // A duplicate key would violate uq_workspace_taxonomies_key during seeding, turning the very first
        // read of a workspace into a 500. Cheap to assert, expensive to discover in production.
        Assert.Equal(
            TaxonomyDefaults.Domains.Count,
            TaxonomyDefaults.Domains.Select(seed => seed.Key).Distinct(StringComparer.OrdinalIgnoreCase).Count());

        Assert.Equal(
            TaxonomyDefaults.WorkTypes.Count,
            TaxonomyDefaults.WorkTypes.Select(seed => seed.Key).Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public void Seeds_CarryAValidBadgeColourAndANonBlankLabel()
    {
        foreach (var seed in TaxonomyDefaults.Domains.Concat(TaxonomyDefaults.WorkTypes))
        {
            Assert.False(string.IsNullOrWhiteSpace(seed.Label));
            Assert.True(TaxonomyDefaults.IsValidColor(seed.ColorHex), $"{seed.Key} thiếu màu hợp lệ");
        }
    }

    [Fact]
    public void Seeds_AreWithinTheColumnLimits()
    {
        foreach (var seed in TaxonomyDefaults.Domains.Concat(TaxonomyDefaults.WorkTypes))
        {
            Assert.InRange(seed.Key.Length, 1, 50);
            Assert.InRange(seed.Label.Length, 1, 120);
        }
    }

    [Theory]
    [InlineData(TaxonomyKind.Domain, 7)]
    [InlineData(TaxonomyKind.WorkType, 6)]
    public void For_ReturnsTheDocumentedDefaultCounts(TaxonomyKind kind, int expected)
    {
        Assert.Equal(expected, TaxonomyDefaults.For(kind).Count);
    }

    // ---- skill slug --------------------------------------------------------------------------

    [Theory]
    [InlineData("csharp", true)]
    [InlineData("dotnet", true)]
    [InlineData("react", true)]
    [InlineData("node.js", true)]
    [InlineData("ui/ux", false)]      // '/' is not in the allowed set
    [InlineData("C#", false)]
    [InlineData("-leading", false)]   // must start with a letter or digit
    [InlineData("has space", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsValidSkillSlug_AcceptsOnlyLowercaseSlugShapedValues(string? slug, bool expected)
    {
        Assert.Equal(expected, TaxonomyDefaults.IsValidSkillSlug(slug));
    }

    [Fact]
    public void IsValidSkillSlug_AcceptsTheDocumentedPunctuationAfterTheFirstCharacter()
    {
        // The allowed set is deliberate: these characters can appear in a skill name a developer would
        // actually type, and all of them are safe as a JSON key or a DOM id.
        Assert.True(TaxonomyDefaults.IsValidSkillSlug("c++"));
        Assert.True(TaxonomyDefaults.IsValidSkillSlug("asp.net-core"));
        Assert.True(TaxonomyDefaults.IsValidSkillSlug("ci_cd"));
        Assert.True(TaxonomyDefaults.IsValidSkillSlug("node.js"));
    }

    [Fact]
    public void IsValidSkillSlug_RejectsSlugsLongerThanTheColumnAllows()
    {
        Assert.False(TaxonomyDefaults.IsValidSkillSlug(new string('a', TaxonomyDefaults.MaxSkillSlugLength + 1)));
        Assert.True(TaxonomyDefaults.IsValidSkillSlug(new string('a', TaxonomyDefaults.MaxSkillSlugLength)));
    }

    // ---- colour ------------------------------------------------------------------------------

    [Theory]
    [InlineData("#1677ff", true)]
    [InlineData("#FFFFFF", true)]
    [InlineData("1677ff", false)]     // missing '#'
    [InlineData("#1677f", false)]     // too short
    [InlineData("#1677fff", false)]   // too long
    [InlineData("#1677gg", false)]    // not hex
    [InlineData(null, false)]
    public void IsValidColor_AcceptsOnlyRrggbbLiterals(string? color, bool expected)
    {
        Assert.Equal(expected, TaxonomyDefaults.IsValidColor(color));
    }

    // ---- primary role ------------------------------------------------------------------------

    [Theory]
    [InlineData("Tech Lead", "Tech Lead")]
    [InlineData("tech lead", "Tech Lead")]        // case-insensitive, canonical spelling returned
    [InlineData("  Backend Dev  ", "Backend Dev")]
    public void CanonicalRole_ResolvesCaseInsensitivelyToTheDocumentedSpelling(string input, string expected)
    {
        Assert.Equal(expected, TaxonomyDefaults.CanonicalRole(input));
    }

    [Theory]
    [InlineData("Chief Vibe Officer")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void CanonicalRole_ReturnsNullForAnythingOutsideTheWhitelist(string? input)
    {
        // A free-for-all would make primary_role useless as a dispatching signal, which is the whole reason
        // the whitelist exists even though the column is free text.
        Assert.Null(TaxonomyDefaults.CanonicalRole(input));
    }

    [Fact]
    public void PrimaryRoles_ContainTheDefaultSoTheColumnDefaultIsAlwaysValid()
    {
        // The column defaults to "Member"; if that were not in the whitelist, a seeded member could never be
        // re-saved without first changing the role — an odd trap for the UI.
        Assert.Contains("Member", TaxonomyDefaults.PrimaryRoles);
    }

    // ---- entry validation --------------------------------------------------------------------

    [Fact]
    public void Validate_NormalisesWhitespaceAndKeepsOrder()
    {
        var normalized = TaxonomyService.Validate(
            [
                new TaxonomyEntryRequest("  Backend  ", "  Máy chủ  ", "  #1677ff  "),
                new TaxonomyEntryRequest("Frontend", "Giao diện", null),
            ],
            TaxonomyKind.Domain,
            "Domains");

        Assert.Equal(2, normalized.Count);
        Assert.Equal("Backend", normalized[0].Key);
        Assert.Equal("Máy chủ", normalized[0].Label);
        Assert.Equal("#1677ff", normalized[0].ColorHex);
        Assert.Null(normalized[1].ColorHex);
    }

    [Fact]
    public void Validate_TreatsNullAsAnEmptyList()
    {
        // A client that omits a kind sends nothing, and the caller then rejects the empty result with a 400 —
        // the validator itself must not throw a NullReferenceException first.
        Assert.Empty(TaxonomyService.Validate(null, TaxonomyKind.Domain, "Domains"));
    }

    [Fact]
    public void Validate_RejectsDuplicateKeysCaseInsensitively()
    {
        var exception = Assert.Throws<BadRequestException>(() => TaxonomyService.Validate(
            [
                new TaxonomyEntryRequest("Backend", "Backend"),
                new TaxonomyEntryRequest("backend", "Trùng"),
            ],
            TaxonomyKind.Domain,
            "Domains"));

        Assert.Contains("backend", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Validate_RejectsBlankKeyBlankLabelBadColorAndTooManyEntries()
    {
        Assert.Throws<BadRequestException>(() => TaxonomyService.Validate(
            [new TaxonomyEntryRequest("  ", "Backend")], TaxonomyKind.Domain, "Domains"));

        Assert.Throws<BadRequestException>(() => TaxonomyService.Validate(
            [new TaxonomyEntryRequest("Backend", "  ")], TaxonomyKind.Domain, "Domains"));

        Assert.Throws<BadRequestException>(() => TaxonomyService.Validate(
            [new TaxonomyEntryRequest("Backend", "Backend", "blue")], TaxonomyKind.Domain, "Domains"));

        var tooMany = Enumerable.Range(0, TaxonomyDefaults.MaxEntriesPerKind + 1)
            .Select(index => new TaxonomyEntryRequest($"key{index}", $"Nhãn {index}"))
            .ToList();

        Assert.Throws<BadRequestException>(() => TaxonomyService.Validate(tooMany, TaxonomyKind.Domain, "Domains"));
    }
}

/// <summary>
/// Giai đoạn 14.7 §A2 — the member capacity-profile validators, exercised directly.
/// <para>
/// The "absent means leave unchanged" contract is the subtle part: <c>null</c> must flow through as
/// <c>null</c>, while an empty collection is a REAL instruction. These tests pin that distinction because the
/// integration suite can only observe its effect, not the rule.
/// </para>
/// </summary>
public sealed class MemberProfileValidationTests
{
    [Fact]
    public void ValidateSkills_ReturnsNullForAnAbsentFieldSoItIsLeftUnchanged()
    {
        Assert.Null(WorkspaceMemberService.ValidateSkills(null));
    }

    [Fact]
    public void ValidateSkills_ReturnsAnEmptyListForAnEmptyFieldSoTheSkillsAreCleared()
    {
        // The distinction that matters: absent ≠ empty.
        var result = WorkspaceMemberService.ValidateSkills([]);

        Assert.NotNull(result);
        Assert.Empty(result!);
    }

    [Fact]
    public void ValidateSkills_LowercasesTrimsAndDeduplicatesKeepingFirstOrder()
    {
        var result = WorkspaceMemberService.ValidateSkills(["CSharp", "dotnet", " CSHARP ", "DotNet"]);

        Assert.Equal(["csharp", "dotnet"], result);
    }

    [Theory]
    [InlineData("csharp")]
    [InlineData("node.js")]
    [InlineData("asp.net-core")]
    public void ValidateSkills_AcceptsSlugShapedValues(string slug)
    {
        Assert.Equal([slug], WorkspaceMemberService.ValidateSkills([slug]));
    }

    [Theory]
    [InlineData("C#")]
    [InlineData("-leading")]
    [InlineData("has space")]
    [InlineData("")]
    [InlineData("   ")]
    public void ValidateSkills_RejectsAnythingThatIsNotASlug(string slug)
    {
        Assert.Throws<BadRequestException>(() => WorkspaceMemberService.ValidateSkills([slug]));
    }

    [Fact]
    public void ValidateSkills_RejectsMoreThanTheDocumentedMaximum()
    {
        var tooMany = Enumerable.Range(0, TaxonomyDefaults.MaxSkillsPerMember + 1)
            .Select(index => $"skill{index}")
            .ToList();

        Assert.Throws<BadRequestException>(() => WorkspaceMemberService.ValidateSkills(tooMany));

        var exactlyAtTheLimit = Enumerable.Range(0, TaxonomyDefaults.MaxSkillsPerMember)
            .Select(index => $"skill{index}")
            .ToList();

        Assert.Equal(TaxonomyDefaults.MaxSkillsPerMember, WorkspaceMemberService.ValidateSkills(exactlyAtTheLimit)!.Count);
    }

    [Fact]
    public void ValidatePrimaryRole_IsNullForAbsentAndCanonicalForPresent()
    {
        Assert.Null(WorkspaceMemberService.ValidatePrimaryRole(null));
        Assert.Equal("QA Engineer", WorkspaceMemberService.ValidatePrimaryRole("qa engineer"));
    }

    [Theory]
    [InlineData("Chief Vibe Officer")]
    [InlineData("")]
    public void ValidatePrimaryRole_RejectsUnknownValues(string role)
    {
        Assert.Throws<BadRequestException>(() => WorkspaceMemberService.ValidatePrimaryRole(role));
    }

    [Fact]
    public void ValidateCapacityWeight_AcceptsTheInclusiveBoundsAndRejectsOutsideThem()
    {
        Assert.Null(WorkspaceMemberService.ValidateCapacityWeight(null));

        Assert.Equal(
            TaxonomyDefaults.MinCapacityWeight,
            WorkspaceMemberService.ValidateCapacityWeight(TaxonomyDefaults.MinCapacityWeight));

        Assert.Equal(
            TaxonomyDefaults.MaxCapacityWeight,
            WorkspaceMemberService.ValidateCapacityWeight(TaxonomyDefaults.MaxCapacityWeight));

        // Zero is the important rejection: it would make the member absorb infinite work in the dispatching
        // maths, which is exactly what the database CHECK also forbids.
        Assert.Throws<BadRequestException>(() => WorkspaceMemberService.ValidateCapacityWeight(0d));
        Assert.Throws<BadRequestException>(
            () => WorkspaceMemberService.ValidateCapacityWeight(TaxonomyDefaults.MaxCapacityWeight + 0.01));
        Assert.Throws<BadRequestException>(() => WorkspaceMemberService.ValidateCapacityWeight(double.NaN));
    }

    [Fact]
    public void ValidateWipLimit_AcceptsTheInclusiveBoundsAndRejectsOutsideThem()
    {
        Assert.Null(WorkspaceMemberService.ValidateWipLimit(null));

        // 0 is a real value meaning "no limit configured", not a rejection.
        Assert.Equal(0, WorkspaceMemberService.ValidateWipLimit(0));
        Assert.Equal(TaxonomyDefaults.MaxWipLimit, WorkspaceMemberService.ValidateWipLimit(TaxonomyDefaults.MaxWipLimit));

        Assert.Throws<BadRequestException>(() => WorkspaceMemberService.ValidateWipLimit(-1));
        Assert.Throws<BadRequestException>(
            () => WorkspaceMemberService.ValidateWipLimit(TaxonomyDefaults.MaxWipLimit + 1));
    }
}
