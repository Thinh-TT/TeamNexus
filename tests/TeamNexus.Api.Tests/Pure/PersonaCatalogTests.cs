using System.Text.RegularExpressions;
using TeamNexus.Modules.Ai.Services.Jev;

namespace TeamNexus.Api.Tests.Pure;

/// <summary>
/// Giai đoạn 14.7 Track B §B1 — <see cref="PersonaCatalog"/>, purely.
/// <para>
/// The catalogue is a closed vocabulary shared by three consumers (the resolver, the chat endpoint and the
/// database CHECK constraint), so the interesting assertions are the ones about <b>identity</b>: which keys
/// exist, and — above all — that <c>general</c> still produces the byte-identical Phase 7 user handle.
/// Renaming that handle would not fail a build; it would silently orphan every agent row that already
/// exists.
/// </para>
/// </summary>
public sealed class PersonaCatalogTests
{
    // ---- the vocabulary -----------------------------------------------------

    [Fact]
    public void All_ContainsExactlyTheFourDocumentedPersonas()
    {
        Assert.Equal(4, PersonaCatalog.All.Count);
        Assert.Equal(
            ["general", "scrum_master", "tech_lead", "qa_security"],
            PersonaCatalog.All.Select(persona => persona.Key).ToList());
    }

    [Fact]
    public void All_KeysAreUniqueAndHaveNoBlankDisplayName()
    {
        var keys = PersonaCatalog.All.Select(persona => persona.Key).ToList();

        // A duplicate key would make `Find` non-deterministic and the unique index unsatisfiable.
        Assert.Equal(keys.Count, keys.Distinct(StringComparer.Ordinal).Count());

        // A blank display name would render an empty persona picker entry.
        Assert.All(PersonaCatalog.All, persona => Assert.False(string.IsNullOrWhiteSpace(persona.DisplayName)));
    }

    [Fact]
    public void All_KeysAreLowercaseSnakeCaseBecauseTheyAreStoredAndMatchedCaseInsensitively()
    {
        // The value is stored in `workspace_members.persona_key` and compared with OrdinalIgnoreCase; a key
        // with uppercase letters would make the stored form depend on who wrote it first.
        foreach (var persona in PersonaCatalog.All)
        {
            Assert.Matches(new Regex("^[a-z][a-z0-9_]*$"), persona.Key);
        }
    }

    [Fact]
    public void Default_IsTheGeneralPersonaSoAnUnawareCallerLandsWherePhase7LeftOff()
    {
        Assert.Equal(PersonaCatalog.GeneralKey, PersonaCatalog.Default.Key);
        Assert.Same(PersonaCatalog.All[0], PersonaCatalog.Default);
    }

    // ---- the general persona is the Phase 7 agent ---------------------------

    [Fact]
    public void General_HasNoSkillsBecauseItsSkillSetIsTheDispatchingInputOfAShippedFeature()
    {
        // §A4's dispatching already ranks the pre-existing agent; inventing skills for it now would change
        // an already-shipped ranking without anyone asking.
        Assert.Empty(PersonaCatalog.SkillsFor(PersonaCatalog.GeneralKey));
        Assert.Empty(PersonaCatalog.Find(PersonaCatalog.GeneralKey)!.Skills);
    }

    [Fact]
    public void General_KeepsTheExactPhase7UserNameWithoutAPersonaSegment()
    {
        // ⛔ The single most important assertion in this file. `ai-agent-{workspaceId:N}` is the handle
        // Phase 7 created and the verification harness looks for; adding a persona segment would silently
        // orphan every existing agent row.
        var workspaceId = Guid.Parse("11111111-2222-3333-4444-555555555555");

        Assert.Equal(
            $"ai-agent-{workspaceId:N}",
            PersonaCatalog.AgentUserName(workspaceId, PersonaCatalog.GeneralKey));

        Assert.DoesNotContain("general", PersonaCatalog.AgentUserName(workspaceId, PersonaCatalog.GeneralKey));
    }

    [Theory]
    [InlineData(PersonaCatalog.ScrumMasterKey)]
    [InlineData(PersonaCatalog.TechLeadKey)]
    [InlineData(PersonaCatalog.QaSecurityKey)]
    public void EveryOtherPersona_GetsItsOwnReadableDistinctHandle(string personaKey)
    {
        var workspaceId = Guid.Parse("11111111-2222-3333-4444-555555555555");

        var userName = PersonaCatalog.AgentUserName(workspaceId, personaKey);

        Assert.Equal($"ai-agent-{personaKey}-{workspaceId:N}", userName);
        Assert.NotEqual(PersonaCatalog.AgentUserName(workspaceId, PersonaCatalog.GeneralKey), userName);
    }

    [Fact]
    public void AgentUserName_IsUniquePerWorkspaceAndPerPersona()
    {
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();

        var handles = new[]
        {
            PersonaCatalog.AgentUserName(first, PersonaCatalog.GeneralKey),
            PersonaCatalog.AgentUserName(first, PersonaCatalog.ScrumMasterKey),
            PersonaCatalog.AgentUserName(first, PersonaCatalog.TechLeadKey),
            PersonaCatalog.AgentUserName(first, PersonaCatalog.QaSecurityKey),
            PersonaCatalog.AgentUserName(second, PersonaCatalog.GeneralKey),
        };

        // Two personas of one workspace, and one persona of two workspaces, must never collide — the unique
        // index depends on it.
        Assert.Equal(handles.Length, handles.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void AgentUserName_WithAnUnknownKey_FallsBackToTheGeneralHandle()
    {
        // Defensive: the resolver validates the key before reaching this, but the fallback keeps the lookup
        // pointed at the one row that always exists rather than at a handle nobody owns.
        var workspaceId = Guid.NewGuid();

        Assert.Equal(
            PersonaCatalog.AgentUserName(workspaceId, PersonaCatalog.GeneralKey),
            PersonaCatalog.AgentUserName(workspaceId, "no-such-persona"));
    }

    // ---- IsKnown / Canonical ------------------------------------------------

    [Theory]
    [InlineData("general")]
    [InlineData("scrum_master")]
    [InlineData("tech_lead")]
    [InlineData("qa_security")]
    [InlineData("GENERAL")]
    [InlineData("Scrum_Master")]
    [InlineData("  tech_lead  ")]
    [InlineData("general ")]
    public void IsKnown_AcceptsEveryKeyCaseInsensitivelyAndAfterTrimming(string candidate)
    {
        // A picker that sends "Tech_Lead" is not a bug worth a 400 when the intent is unambiguous.
        Assert.True(PersonaCatalog.IsKnown(candidate));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("architect")]
    [InlineData("ai-agent")]
    [InlineData("general;drop")]
    public void IsKnown_RejectsBlankAndUnknownValues(string? candidate)
    {
        Assert.False(PersonaCatalog.IsKnown(candidate));
    }

    [Fact]
    public void Canonical_ReturnsTheCatalogCasingNotTheCallers()
    {
        // The stored value must be the catalogue's, otherwise two rows could differ only in casing and the
        // unique index (which is case-sensitive) would accept both.
        Assert.Equal("scrum_master", PersonaCatalog.Canonical("  SCRUM_MASTER  "));
        Assert.Equal("qa_security", PersonaCatalog.Canonical("Qa_Security"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("architect")]
    public void Canonical_ReturnsNullForUnknownRatherThanFallingBackToGeneral(string? candidate)
    {
        // ⛔ The design decision this pins: a silent fallback would make "the picker sent a stale key"
        // indistinguishable from "the default persona was used", which is the class of failure the phase
        // exists to eliminate.
        Assert.Null(PersonaCatalog.Canonical(candidate));
    }

    [Fact]
    public void Find_ReturnsTheSameInstanceTheCatalogAdvertises()
    {
        foreach (var persona in PersonaCatalog.All)
        {
            Assert.Same(persona, PersonaCatalog.Find(persona.Key));
            Assert.Same(persona, PersonaCatalog.Find(persona.Key.ToUpperInvariant()));
        }
    }

    [Fact]
    public void Find_ReturnsNullForAnUnknownKey()
    {
        Assert.Null(PersonaCatalog.Find("architect"));
        Assert.Null(PersonaCatalog.Find(null));
    }

    // ---- display names and skills -------------------------------------------

    [Fact]
    public void DisplayNameFor_ReturnsThePersonaNameAndTheGeneralNameForUnknownKeys()
    {
        Assert.Equal("AI Tech Lead", PersonaCatalog.DisplayNameFor(PersonaCatalog.TechLeadKey));
        Assert.Equal(
            PersonaCatalog.Find(PersonaCatalog.GeneralKey)!.DisplayName,
            PersonaCatalog.DisplayNameFor("architect"));
    }

    [Fact]
    public void SkillsFor_ReturnsANonEmptyDistinctSlugSetForEverySpecialistPersona()
    {
        foreach (var persona in PersonaCatalog.All.Where(p => p.Key != PersonaCatalog.GeneralKey))
        {
            var skills = PersonaCatalog.SkillsFor(persona.Key);

            Assert.NotEmpty(skills);
            Assert.Equal(skills.Count, skills.Distinct(StringComparer.OrdinalIgnoreCase).Count());
            Assert.All(skills, skill => Assert.False(string.IsNullOrWhiteSpace(skill)));
            Assert.All(skills, skill => Assert.Equal(skill.ToLowerInvariant(), skill));
        }
    }

    [Fact]
    public void SkillsFor_ReturnsEmptyForAnUnknownKey()
    {
        Assert.Empty(PersonaCatalog.SkillsFor("architect"));
    }

    [Fact]
    public void AgentPersona_IsAValueWithAllThreeParts()
    {
        var persona = PersonaCatalog.Find(PersonaCatalog.QaSecurityKey);

        Assert.NotNull(persona);
        Assert.Equal("qa_security", persona!.Key);
        Assert.Equal("AI QA & Security", persona.DisplayName);
        Assert.Contains("security-audit", persona.Skills);
    }
}
