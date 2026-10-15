using TeamNexus.Persistence.Data.Entities;

namespace TeamNexus.Modules.Board.Services;

/// <summary>
/// The workspace vocabulary TeamNexus ships with, plus the validation rules for the fields that reference
/// it (Giai đoạn 14.7 §A2, Trụ cột 1).
/// <para>
/// Deliberately a <b>pure static class with no I/O</b>: these values are seeded into a workspace on first
/// read by <c>TaxonomyService</c> and are also the whitelist the profile-update path validates against, so
/// keeping them in one place is what stops the seeded vocabulary and the accepted values from drifting.
/// </para>
/// </summary>
public static class TaxonomyDefaults
{
    /// <summary>Maximum entries of one kind per workspace.</summary>
    public const int MaxEntriesPerKind = 40;

    /// <summary>Maximum skills one member may declare.</summary>
    public const int MaxSkillsPerMember = 20;

    public const int MaxSkillSlugLength = 40;

    public const int MaxPrimaryRoleLength = 50;

    public const double MinCapacityWeight = 0.1;

    public const double MaxCapacityWeight = 3.0;

    public const int MinWipLimit = 0;

    public const int MaxWipLimit = 50;

    /// <summary>
    /// Technical domains. Ordered as they are presented: the most common first, so the model's option list
    /// puts the likely answers early.
    /// </summary>
    public static IReadOnlyList<TaxonomySeed> Domains { get; } =
    [
        new("Backend", "Backend", "#1677ff"),
        new("Frontend", "Frontend", "#722ed1"),
        new("DevOps", "DevOps / Infra", "#13c2c2"),
        new("Database", "Database", "#fa8c16"),
        new("Mobile", "Mobile", "#eb2f96"),
        new("UI/UX", "UI/UX", "#a0d911"),
        new("QA/Testing", "QA / Testing", "#faad14"),
    ];

    /// <summary>Kinds of work. The list is the <c>Choice</c> option set for the work-type question.</summary>
    public static IReadOnlyList<TaxonomySeed> WorkTypes { get; } =
    [
        new("Feature", "Feature", "#1677ff"),
        new("Bug", "Bug", "#ff4d4f"),
        new("Refactor", "Refactor", "#13c2c2"),
        new("TechDebt", "Tech Debt", "#fa8c16"),
        new("Security", "Security", "#cf1322"),
        new("Documentation", "Documentation", "#8c8c8c"),
    ];

    /// <summary>
    /// Roles accepted for <c>workspace_members.primary_role</c>.
    /// <para>
    /// A whitelist rather than an enum: the column is free text so that adding a role never needs a
    /// migration, but a free-for-all would make the value useless as a dispatching signal.
    /// </para>
    /// </summary>
    public static IReadOnlyList<string> PrimaryRoles { get; } =
    [
        "Member",
        "Tech Lead",
        "Backend Dev",
        "Frontend Dev",
        "Fullstack Dev",
        "Mobile Dev",
        "QA Engineer",
        "DevOps",
        "Scrum Master",
        "Product Owner",
        "Designer",
    ];

    /// <summary>
    /// A skill slug is lowercase ASCII with digits and a small punctuation set — the same shape as the
    /// frontend's key names, so a slug can be used as a React key or an icon name without escaping.
    /// Compiled once because every profile update validates up to <see cref="MaxSkillsPerMember"/> of them.
    /// </summary>
    private static readonly System.Text.RegularExpressions.Regex SkillSlugPattern = new(
        @"^[a-z0-9][a-z0-9+._-]*$",
        System.Text.RegularExpressions.RegexOptions.Compiled);

    /// <summary>Badge colour as <c>#RRGGBB</c>; the alpha form is deliberately not accepted.</summary>
    private static readonly System.Text.RegularExpressions.Regex ColorPattern = new(
        "^#[0-9A-Fa-f]{6}$",
        System.Text.RegularExpressions.RegexOptions.Compiled);

    /// <summary>True when <paramref name="slug"/> is a usable skill slug and not too long.</summary>
    public static bool IsValidSkillSlug(string? slug)
        => !string.IsNullOrWhiteSpace(slug)
           && slug.Length <= MaxSkillSlugLength
           && SkillSlugPattern.IsMatch(slug);

    /// <summary>True when <paramref name="colorHex"/> is a <c>#RRGGBB</c> literal.</summary>
    public static bool IsValidColor(string? colorHex)
        => !string.IsNullOrWhiteSpace(colorHex) && ColorPattern.IsMatch(colorHex);

    /// <summary>
    /// Canonical casing of a role from <see cref="PrimaryRoles"/>, or <c>null</c> when it is not in the
    /// vocabulary. Matching is case-insensitive so a client sending "backend dev" still resolves.
    /// </summary>
    public static string? CanonicalRole(string? role)
    {
        if (string.IsNullOrWhiteSpace(role))
        {
            return null;
        }

        var trimmed = role.Trim();

        return PrimaryRoles.FirstOrDefault(
            known => string.Equals(known, trimmed, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>The default vocabulary for one kind, in display order.</summary>
    public static IReadOnlyList<TaxonomySeed> For(TaxonomyKind kind)
        => kind == TaxonomyKind.WorkType ? WorkTypes : Domains;
}

/// <summary>
/// One default taxonomy entry. <paramref name="Key"/> is the machine value, <paramref name="Label"/> the
/// human name, <paramref name="ColorHex"/> the badge colour.
/// </summary>
public sealed record TaxonomySeed(string Key, string Label, string? ColorHex);
