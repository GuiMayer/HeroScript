using Core.Common;
using Core.Content;

namespace Core.Run.Content;

public interface IRunCardResolver
{
    Result<EffectiveCardDefinition> Resolve(RunState run, CardInstanceState instance);
}

/// <summary>
/// Resolves one card instance exclusively from the immutable content revision
/// pinned by its run. Consumers do not need access to mutable catalogs or to
/// know how authored containers and permanent upgrades are composed.
/// </summary>
public sealed class RunCardResolver : IRunCardResolver
{
    private readonly IContentRuntimeResolver _runtimes;
    private readonly ICardContentCompiler _compiler;
    private readonly IEffectiveCardResolver _effectiveCards;

    public RunCardResolver(
        IContentRuntimeResolver runtimes,
        ICardContentCompiler compiler,
        IEffectiveCardResolver effectiveCards)
    {
        _runtimes = runtimes ?? throw new ArgumentNullException(nameof(runtimes));
        _compiler = compiler ?? throw new ArgumentNullException(nameof(compiler));
        _effectiveCards = effectiveCards ?? throw new ArgumentNullException(nameof(effectiveCards));
    }

    public Result<EffectiveCardDefinition> Resolve(RunState run, CardInstanceState instance)
    {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(instance);
        var runtime = _runtimes.Resolve(run.Determinism.ContentRevision, run.ConfigName);
        if (runtime.IsFailure)
            return Result<EffectiveCardDefinition>.Failure(runtime.Error);
        var compiled = _compiler.Compile(instance.DefinitionId, runtime.Value);
        if (compiled.IsFailure)
            return Result<EffectiveCardDefinition>.Failure(compiled.Error);
        return _effectiveCards.Resolve(compiled.Value, instance);
    }
}
