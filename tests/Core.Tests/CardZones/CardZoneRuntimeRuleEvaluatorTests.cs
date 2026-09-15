using Core.CardZones;
using Core.Common;
using Core.Math;
using Core.Run;
using Xunit;

namespace Core.Tests.CardZones;

public sealed class CardZoneRuntimeRuleEvaluatorTests
{
    [Fact]
    public void EvaluateCount_UsesPinnedRevisionAndRejectsFractionalOrOverflowingCounts()
    {
        var formulas = new RecordingFormulas();
        var rules = new CardZoneRuntimeRuleEvaluator(formulas, new RecordingCards());
        var context = new CardZoneFlowContext
        {
            ContentRevision = "revision-a",
            Variables = new Dictionary<string, double> { ["drawCount"] = 5 }
        };

        var count = rules.EvaluateCount("drawCount", context);
        Assert.True(count.IsSuccess, count.IsFailure ? count.Error : null);
        Assert.Equal(5, count.Value);
        Assert.Equal("revision-a", formulas.LastRevision);
        Assert.Equal(5f, formulas.LastVariables!["drawCount"]);

        Assert.True(rules.EvaluateCount("1.5", context).IsFailure);
        Assert.True(rules.EvaluateCount("2147483648", context).IsFailure);
        Assert.True(rules.EvaluateCount("-1", context).IsFailure);
    }

    [Fact]
    public void Matches_UsesCardMetadataForTagsAndPerInstanceUpgradeCountForConditions()
    {
        var formulas = new RecordingFormulas();
        var cards = new RecordingCards();
        var rules = new CardZoneRuntimeRuleEvaluator(formulas, cards);
        var instance = new CardInstanceState
        {
            CardInstanceId = Guid.NewGuid(),
            DefinitionId = "fireball",
            Upgrades = [new CardUpgradeState { UpgradeId = "fireball-plus" }]
        };
        var context = new CardZoneFlowContext { ContentRevision = "revision-a" };

        var tagged = rules.Matches(instance, new CardZoneSelectionDefinition
        {
            Strategy = CardZoneSelectionStrategy.ByTags,
            RequiredTags = ["spell", "fire"]
        }, context);
        Assert.True(tagged.IsSuccess && tagged.Value);
        Assert.Same(instance, cards.LastInstance);
        Assert.Equal("revision-a", cards.LastContext?.ContentRevision);

        var conditional = rules.Matches(instance, new CardZoneSelectionDefinition
        {
            Strategy = CardZoneSelectionStrategy.ByCondition,
            Condition = "cardUpgradeCount"
        }, context);
        Assert.True(conditional.IsSuccess && conditional.Value);
        Assert.Equal(1f, formulas.LastVariables!["cardUpgradeCount"]);
    }

    private sealed class RecordingCards : ICardZoneCardMetadataResolver
    {
        public CardInstanceState? LastInstance { get; private set; }
        public CardZoneFlowContext? LastContext { get; private set; }

        public Result<IReadOnlyList<string>> ResolveTags(CardInstanceState instance, CardZoneFlowContext context)
        {
            LastInstance = instance;
            LastContext = context;
            return Result<IReadOnlyList<string>>.Success(["spell", "fire"]);
        }
    }

    private sealed class RecordingFormulas : IRevisionedRuntimeFormulaEvaluator
    {
        public string? LastRevision { get; private set; }
        public Dictionary<string, float>? LastVariables { get; private set; }

        public Result<float> Evaluate(string expressionOrFormulaId, Dictionary<string, float>? variables = null,
            float initialValue = 0f) => Resolve(expressionOrFormulaId, variables);

        public Result<float> EvaluateAtRevision(string expressionOrFormulaId, string contentRevision,
            Dictionary<string, float>? variables = null, float initialValue = 0f)
        {
            LastRevision = contentRevision;
            return Resolve(expressionOrFormulaId, variables);
        }

        private Result<float> Resolve(string expression, Dictionary<string, float>? variables)
        {
            LastVariables = variables;
            if (variables?.TryGetValue(expression, out var value) == true)
                return Result<float>.Success(value);
            return float.TryParse(expression, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out value)
                ? Result<float>.Success(value)
                : Result<float>.Failure("Unknown formula");
        }
    }
}
