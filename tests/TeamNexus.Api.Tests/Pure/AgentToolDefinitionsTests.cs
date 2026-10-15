using System.Text.Json;
using TeamNexus.Modules.Ai.Options;
using TeamNexus.Modules.Ai.Services;
using TeamNexus.Modules.Ai.Services.Agent;

namespace TeamNexus.Api.Tests.Pure;

/// <summary>
/// Giai đoạn 14.7 Track B §B2 — the agent's tool whitelist, purely.
/// <para>
/// <b>The guardrail half is the reason this file exists.</b> "One more tool" is exactly the change that
/// quietly raises what a run may cost, so the budgets are asserted against the resolved production values and
/// each schema is asserted to be a usable contract (an array without <c>items</c>, or a required key that is
/// not a declared property, is a schema the model will fail against in production).
/// </para>
/// </summary>
public sealed class AgentToolDefinitionsTests
{
    private static AgentEffectiveOptions Defaults => new AgentOptions().Effective;

    // ---- the whitelist ------------------------------------------------------

    [Fact]
    public void All_AdvertisesExactlySevenToolsAfterTheThreeNewOnesWereAdded()
    {
        // Four from Phase 7 plus three from Track B. A silent addition is a cost change, which is why the
        // count is pinned rather than merely asserted to be "at least".
        Assert.Equal(7, AgentToolDefinitions.All.Count);
    }

    [Fact]
    public void All_NamesAreUnique()
    {
        // The registry keys its dictionary by name, so a duplicate would make dispatch arbitrary.
        var names = AgentToolDefinitions.All.Select(tool => tool.Name).ToList();

        Assert.Equal(names.Count, names.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public void All_ContainsTheThreeTrackBToolsAndStillTheFourPhase7Ones()
    {
        var names = AgentToolDefinitions.All.Select(tool => tool.Name).ToList();

        Assert.Contains(AgentToolDefinitions.InspectMemberWorkloadName, names);
        Assert.Contains(AgentToolDefinitions.MatchSkillsName, names);
        Assert.Contains(AgentToolDefinitions.ValidateAcceptanceCriteriaName, names);

        Assert.Contains(AgentToolDefinitions.SearchSystemDataName, names);
        Assert.Contains(AgentToolDefinitions.WebSearchName, names);
        Assert.Contains(AgentToolDefinitions.DraftOutputName, names);
        Assert.Contains(AgentToolDefinitions.RequestClarificationName, names);
    }

    [Fact]
    public void IsWhitelisted_AcceptsEveryAdvertisedNameAndRejectsEverythingElse()
    {
        Assert.All(AgentToolDefinitions.All, tool => Assert.True(AgentToolDefinitions.IsWhitelisted(tool.Name)));

        // Case-insensitive: models re-case tool names constantly.
        Assert.True(AgentToolDefinitions.IsWhitelisted(AgentToolDefinitions.MatchSkillsName.ToUpperInvariant()));

        Assert.False(AgentToolDefinitions.IsWhitelisted("DeleteEverything"));
        Assert.False(AgentToolDefinitions.IsWhitelisted(null));
        Assert.False(AgentToolDefinitions.IsWhitelisted("MatchSkills "));
    }

    // ---- guardrails do not widen with the tool count ------------------------

    [Fact]
    public void TheFourGuardrails_AreStillTheProductionValuesAfterAddingThreeTools()
    {
        // ⛔ The assertion this file was written for. The three new tools share the SAME budgets as the
        // original four — the registry counts calls, it does not multiply budgets. A change to any of these
        // four numbers would silently let a run cost more than Phase 7 chose.
        var effective = Defaults;

        Assert.Equal(15, effective.MaxToolCalls);
        Assert.Equal(300, effective.RunTimeoutSeconds);
        Assert.Equal(50_000, effective.MaxRunTokens);
        Assert.Equal(20, effective.MaxRunLlmCalls);
    }

    [Fact]
    public void SevenTools_AllFitInsideTheSingleToolCallBudget()
    {
        // The cheap sanity check on the relationship the numbers above encode: one full sweep of every
        // advertised tool must still be affordable inside `Agent:MaxToolCalls`.
        Assert.True(AgentToolDefinitions.All.Count <= Defaults.MaxToolCalls);
    }

    [Fact]
    public void AddingToolsDidNotRaiseTheToolResultCeilings()
    {
        // The other two numbers a "one more tool" change tends to move: how large ONE result may be and how
        // much of the conversation the results may occupy.
        Assert.Equal(4_000, Defaults.MaxToolResultChars);
        Assert.Equal(40_000, Defaults.MaxTotalToolResultChars);
    }

    // ---- schema shape -------------------------------------------------------

    [Fact]
    public void EveryTool_HasAReadableNameAndANonEmptyDescriptionInVietnamese()
    {
        Assert.All(AgentToolDefinitions.All, tool =>
        {
            Assert.False(string.IsNullOrWhiteSpace(tool.Name));
            Assert.False(string.IsNullOrWhiteSpace(tool.Description));
        });
    }

    [Fact]
    public void EveryTool_DescribesAnObjectWithNoAdditionalProperties()
    {
        // The model contract is reviewable text, so this is asserted on the parsed JSON rather than trusted.
        foreach (var tool in AgentToolDefinitions.All)
        {
            using var document = JsonDocument.Parse(tool.ParametersJsonSchema);
            var root = document.RootElement;

            Assert.Equal("object", root.GetProperty("type").GetString());
            Assert.False(root.GetProperty("additionalProperties").GetBoolean());
            Assert.Equal(JsonValueKind.Object, root.GetProperty("properties").ValueKind);
            Assert.Equal(JsonValueKind.Array, root.GetProperty("required").ValueKind);
        }
    }

    [Fact]
    public void EveryRequiredProperty_IsActuallyDeclared()
    {
        // A required name that is not in `properties` makes the schema unsatisfiable: the model must send a
        // value it was never told about.
        foreach (var tool in AgentToolDefinitions.All)
        {
            using var document = JsonDocument.Parse(tool.ParametersJsonSchema);
            var root = document.RootElement;

            var declared = root.GetProperty("properties").EnumerateObject().Select(p => p.Name).ToHashSet(StringComparer.Ordinal);

            foreach (var required in root.GetProperty("required").EnumerateArray())
            {
                var name = required.GetString();

                Assert.NotNull(name);
                Assert.True(declared.Contains(name!), $"Tool {tool.Name}: required '{name}' không có trong properties.");
            }
        }
    }

    [Fact]
    public void EveryDeclaredProperty_HasATypeAndADescription()
    {
        foreach (var tool in AgentToolDefinitions.All)
        {
            using var document = JsonDocument.Parse(tool.ParametersJsonSchema);
            var properties = document.RootElement.GetProperty("properties");

            foreach (var property in properties.EnumerateObject())
            {
                Assert.True(
                    property.Value.TryGetProperty("type", out var type),
                    $"Tool {tool.Name}: '{property.Name}' thiếu 'type'.");

                Assert.False(string.IsNullOrWhiteSpace(type.GetString()));

                Assert.True(
                    property.Value.TryGetProperty("description", out var description),
                    $"Tool {tool.Name}: '{property.Name}' thiếu 'description'.");

                Assert.False(string.IsNullOrWhiteSpace(description.GetString()));
            }
        }
    }

    [Fact]
    public void EveryArrayProperty_DeclaresItsItemType()
    {
        // An array without `items` lets the model send objects, and every consumer would then have to defend
        // against it. `MatchSkills.skills` is the first array property in the whitelist.
        foreach (var tool in AgentToolDefinitions.All)
        {
            using var document = JsonDocument.Parse(tool.ParametersJsonSchema);
            var properties = document.RootElement.GetProperty("properties");

            foreach (var property in properties.EnumerateObject())
            {
                if (!string.Equals(property.Value.GetProperty("type").GetString(), "array", StringComparison.Ordinal))
                {
                    continue;
                }

                Assert.True(
                    property.Value.TryGetProperty("items", out var items),
                    $"Tool {tool.Name}: mảng '{property.Name}' thiếu 'items'.");

                Assert.False(string.IsNullOrWhiteSpace(items.GetProperty("type").GetString()));
            }
        }
    }

    [Fact]
    public void InvalidNumericBounds_AreNeverInverted()
    {
        foreach (var tool in AgentToolDefinitions.All)
        {
            using var document = JsonDocument.Parse(tool.ParametersJsonSchema);
            var properties = document.RootElement.GetProperty("properties");

            foreach (var property in properties.EnumerateObject())
            {
                if (!property.Value.TryGetProperty("minimum", out var minimum)
                    || !property.Value.TryGetProperty("maximum", out var maximum))
                {
                    continue;
                }

                Assert.True(
                    minimum.GetInt32() <= maximum.GetInt32(),
                    $"Tool {tool.Name}: '{property.Name}' có minimum > maximum.");
            }
        }
    }

    // ---- the three new contracts --------------------------------------------

    [Fact]
    public void InspectMemberWorkload_RequiresExactlyAUserId()
    {
        var tool = Single(AgentToolDefinitions.InspectMemberWorkloadName);

        var (declared, required) = Declared(tool);

        Assert.Equal(["userId"], declared);
        Assert.Equal(["userId"], required);
    }

    [Fact]
    public void MatchSkills_RequiresTheSkillListAndCapsTheLimitAtTheDeclaredMaximum()
    {
        var tool = Single(AgentToolDefinitions.MatchSkillsName);

        var (declared, required) = Declared(tool);

        Assert.Equal(["skills", "limit"], declared);
        Assert.Equal(["skills"], required);

        using var document = JsonDocument.Parse(tool.ParametersJsonSchema);
        var properties = document.RootElement.GetProperty("properties");

        var skills = properties.GetProperty("skills");

        Assert.Equal("array", skills.GetProperty("type").GetString());
        Assert.True(skills.GetProperty("maxItems").GetInt32() > 0);

        var limit = properties.GetProperty("limit");

        // ⛔ The schema bound and the service's clamp must agree: a schema that offered 50 would let the model
        // ask for more members than the tool will ever return.
        Assert.Equal(1, limit.GetProperty("minimum").GetInt32());
        Assert.Equal(AgentToolDefinitions.MaxSkillMatches, limit.GetProperty("maximum").GetInt32());
    }

    [Fact]
    public void ValidateAcceptanceCriteria_RequiresTheDraftAndTreatsCriteriaAsOptional()
    {
        var tool = Single(AgentToolDefinitions.ValidateAcceptanceCriteriaName);

        var (declared, required) = Declared(tool);

        Assert.Equal(["draft", "criteria"], declared);
        Assert.Equal(["draft"], required);
    }

    [Fact]
    public void ValidateAcceptanceCriteria_SaysOutLoudThatItIsNotAWayToFinishARun()
    {
        // The failure this prevents: a model that reads a validation verdict as approval stops without
        // calling DraftOutput/RequestClarification, and a run that ends without either has no output.
        var tool = Single(AgentToolDefinitions.ValidateAcceptanceCriteriaName);

        Assert.Contains("KHÔNG dùng tool", tool.Description, StringComparison.Ordinal);
        Assert.Contains("kết thúc", tool.Description, StringComparison.Ordinal);
    }

    [Fact]
    public void SearchSystemData_KeepsItsScopeEnumInTheDocumentedOrder()
    {
        // The enum order is part of the wire contract the model reads; the service's own whitelist is asserted
        // in its own suite.
        Assert.Equal(["task", "board", "workspace"], AgentToolDefinitions.SearchScopes);

        var tool = Single(AgentToolDefinitions.SearchSystemDataName);

        using var document = JsonDocument.Parse(tool.ParametersJsonSchema);
        var scope = document.RootElement.GetProperty("properties").GetProperty("scope");

        Assert.Equal(
            AgentToolDefinitions.SearchScopes,
            scope.GetProperty("enum").EnumerateArray().Select(v => v.GetString()!).ToList());
    }

    [Theory]
    [InlineData(AgentToolDefinitions.SearchSystemDataName)]
    [InlineData(AgentToolDefinitions.WebSearchName)]
    [InlineData(AgentToolDefinitions.InspectMemberWorkloadName)]
    [InlineData(AgentToolDefinitions.MatchSkillsName)]
    [InlineData(AgentToolDefinitions.ValidateAcceptanceCriteriaName)]
    [InlineData(AgentToolDefinitions.DraftOutputName)]
    [InlineData(AgentToolDefinitions.RequestClarificationName)]
    public void EveryToolName_HasAStableConstantMatchingTheAdvertisedName(string constant)
    {
        // In production the loops in AgentToolRegistry branch on THESE constants; a schema advertised under a
        // different string would make the branch unreachable.
        Assert.Contains(AgentToolDefinitions.All, tool => string.Equals(tool.Name, constant, StringComparison.Ordinal));
    }

    // ---- helpers ------------------------------------------------------------

    private static AiToolDefinition Single(string name)
        => Assert.Single(AgentToolDefinitions.All, tool => string.Equals(tool.Name, name, StringComparison.Ordinal));

    private static (List<string> Declared, List<string> Required) Declared(AiToolDefinition tool)
    {
        using var document = JsonDocument.Parse(tool.ParametersJsonSchema);

        var declared = document.RootElement
            .GetProperty("properties")
            .EnumerateObject()
            .Select(property => property.Name)
            .ToList();

        var required = document.RootElement
            .GetProperty("required")
            .EnumerateArray()
            .Select(value => value.GetString()!)
            .ToList();

        return (declared, required);
    }
}
