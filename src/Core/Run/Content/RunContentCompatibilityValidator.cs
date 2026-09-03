using Core.Common;
using Core.Content;

namespace Core.Run.Content;

/// <summary>
/// Validates state-pinned card instances against a candidate content runtime.
/// Development hot reload is rejected atomically when a new base container no
/// longer accepts an upgrade patch already owned by the run.
/// </summary>
public static class RunContentCompatibilityValidator
{
    public static Result ValidateForActivation(RunState run, ContentRuntime runtime)
    {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(runtime);
        var compiler = new CardContentCompiler();
        var effectiveCards = new EffectiveCardResolver();
        foreach (var instance in run.Deck.CardInstances.Values
                     .OrderBy(card => card.CardInstanceId))
        {
            var compiled = compiler.Compile(instance.DefinitionId, runtime);
            if (compiled.IsFailure)
            {
                return Result.Failure(
                    $"Card instance {instance.CardInstanceId} cannot activate target content: {compiled.Error}");
            }
            var effective = effectiveCards.Resolve(compiled.Value, instance);
            if (effective.IsFailure)
            {
                return Result.Failure(
                    $"Card instance {instance.CardInstanceId} cannot activate target content: {effective.Error}");
            }
        }
        return Result.Success();
    }
}
