using Core.Common;
using Core.Logging;
using Core.Math;
using Core.Content;
using System.Collections.Immutable;
using System.Text.Json;
using Moq;
using Xunit;

namespace Core.Tests.Math;

public class RuntimeFormulaEvaluatorTests
{
    private readonly Mock<IMathEngine> _mathEngine = new();
    private readonly RuntimeFormulaEvaluator _evaluator;

    public RuntimeFormulaEvaluatorTests()
    {
        _evaluator = new RuntimeFormulaEvaluator(
            _mathEngine.Object,
            new ExpressionEvaluator(NullLogger.Instance),
            NullLogger.Instance);
    }

    [Fact]
    public void Evaluate_WithFormulaId_UsesMathEngine()
    {
        _mathEngine.Setup(m => m.FormulaExists("TEST_FORMULA")).Returns(true);
        _mathEngine.Setup(m => m.BuildFromFormula(
                "TEST_FORMULA",
                10f,
                It.Is<Dictionary<string, float>>(v => v["bonus"] == 5f)))
            .Returns(new MathExpression(10f).Add(5f));

        var result = _evaluator.Evaluate("TEST_FORMULA", new Dictionary<string, float> { ["bonus"] = 5f }, 10f);

        Assert.True(result.IsSuccess);
        Assert.Equal(15f, result.Value);
        _mathEngine.Verify(m => m.BuildFromFormula("TEST_FORMULA", 10f, It.IsAny<Dictionary<string, float>>()), Times.Once);
    }

    [Fact]
    public void Evaluate_WithInlineExpression_UsesExpressionEvaluatorSemantics()
    {
        _mathEngine.Setup(m => m.FormulaExists(It.IsAny<string>())).Returns(false);

        var result = _evaluator.Evaluate("stacks * 0.25", new Dictionary<string, float> { ["stacks"] = 4f });

        Assert.True(result.IsSuccess);
        Assert.Equal(1f, result.Value);
        _mathEngine.Verify(m => m.BuildFromFormula(It.IsAny<string>(), It.IsAny<float>(), It.IsAny<Dictionary<string, float>>()), Times.Never);
    }

    [Fact]
    public void Evaluate_WithInlineDivisionByZero_ReturnsFailure()
    {
        _mathEngine.Setup(m => m.FormulaExists(It.IsAny<string>())).Returns(false);

        var result = _evaluator.Evaluate("10 / zero", new Dictionary<string, float> { ["zero"] = 0f });

        Assert.True(result.IsFailure);
        Assert.Contains("Division by zero", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Evaluate_WithUnsupportedInlineOperator_ReturnsFailure()
    {
        _mathEngine.Setup(m => m.FormulaExists(It.IsAny<string>())).Returns(false);

        var result = _evaluator.Evaluate("stacks ^ 2", new Dictionary<string, float> { ["stacks"] = 3f });

        Assert.True(result.IsFailure);
        Assert.Contains("Unsupported inline expression operator", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Evaluate_WithUnknownFormulaIdLikeToken_ReturnsFailure()
    {
        _mathEngine.Setup(m => m.FormulaExists("MISSING_FORMULA")).Returns(false);

        var result = _evaluator.Evaluate("MISSING_FORMULA");

        Assert.True(result.IsFailure);
        Assert.Contains("Invalid inline expression operand", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Evaluate_WithContentRevision_UsesFormulaFromThatImmutableRuntime()
    {
        var revision = new string('a', 64);
        var definition = new FormulaDefinition
        {
            Params = new Dictionary<string, float> { ["BONUS"] = 7f },
            Operations = [new OperationDefinition { Op = "ADD", Value = "params.BONUS" }]
        };
        var path = "Pipelines/MathFormulas.json";
        var bundle = new ContentBundle
        {
            Manifest = new ContentManifest
            {
                ConfigName = "default",
                Revision = revision,
                Artifacts = [new ContentArtifactManifest { Kind = "formulas", Path = path }]
            },
            Artifacts = ImmutableDictionary<string, JsonElement>.Empty
                .WithComparers(StringComparer.Ordinal)
                .Add(path, JsonSerializer.SerializeToElement(
                    new Dictionary<string, FormulaDefinition> { ["BONUS_FORMULA"] = definition }))
        };
        var runtime = ContentRuntime.Create(bundle).Value;
        var runtimes = new Mock<IContentRuntimeResolver>();
        runtimes.Setup(service => service.Resolve(revision, null))
            .Returns(Result<ContentRuntime>.Success(runtime));
        _mathEngine.Setup(engine => engine.BuildFromDefinition(
                "BONUS_FORMULA",
                It.Is<FormulaDefinition>(formula => formula.Params["BONUS"] == 7f),
                3f,
                It.IsAny<Dictionary<string, float>>()))
            .Returns(new MathExpression(3f).Add(7f));
        var evaluator = new RuntimeFormulaEvaluator(
            _mathEngine.Object,
            new ExpressionEvaluator(NullLogger.Instance),
            NullLogger.Instance,
            runtimes.Object);

        var result = evaluator.EvaluateAtRevision(
            "BONUS_FORMULA",
            revision,
            initialValue: 3f);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error : null);
        Assert.Equal(10f, result.Value);
        _mathEngine.Verify(engine => engine.BuildFromFormula(
            It.IsAny<string>(), It.IsAny<float>(), It.IsAny<Dictionary<string, float>>()), Times.Never);
    }
}
