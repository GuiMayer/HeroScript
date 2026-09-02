using Core.Combat.TurnPhase;
using Core.Config;
using Core.Logging;
using Moq;
using Xunit;

namespace Core.Tests.Combat.TurnPhase;

public sealed class PhaseSystemFactoryTests
{
    [Fact]
    public void CreateFromJson_BuildsSystemAroundDataDrivenSequence()
    {
        var factory = new PhaseSystemFactory(
            new ConsoleLogger(nameof(PhaseSystemFactoryTests)),
            Mock.Of<IConfigManager>(),
            Mock.Of<IResourceLoader>());

        var result = factory.CreateFromJson(PhaseSequenceLoaderTests.ValidJson);

        Assert.True(result.IsSuccess);
        Assert.Equal("planning_window", result.Value.Sequence.Phases[1].PhaseId);
        Assert.NotNull(result.Value.PhaseManager);
        Assert.NotNull(result.Value.PrioritySystem);
        Assert.NotNull(result.Value.StackManager);
    }

    [Fact]
    public void CreateFromJson_RejectsOldEnumShapedConfiguration()
    {
        var factory = new PhaseSystemFactory(
            new ConsoleLogger(nameof(PhaseSystemFactoryTests)),
            Mock.Of<IConfigManager>(),
            Mock.Of<IResourceLoader>());

        var result = factory.CreateFromJson("""{ "phases": ["MAIN_1"] }""");

        Assert.True(result.IsFailure);
    }
}
