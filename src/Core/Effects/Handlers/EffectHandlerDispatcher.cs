using System.Collections.Immutable;

namespace Core.Effects.Handlers;

public sealed class EffectHandlerDispatcher
{
    private readonly ImmutableDictionary<EffectType, IEffectHandler> _handlers;

    public EffectHandlerDispatcher(IEnumerable<IEffectHandler> handlers)
    {
        var builder = ImmutableDictionary.CreateBuilder<EffectType, IEffectHandler>();
        foreach (var handler in handlers)
        {
            foreach (var type in handler.SupportedTypes)
            {
                if (builder.ContainsKey(type))
                    throw new InvalidOperationException($"Multiple handlers registered for {type}");
                builder[type] = handler;
            }
        }
        _handlers = builder.ToImmutable();
    }

    public EffectResult Execute(EffectExecutionRequest request)
    {
        return _handlers.TryGetValue(request.Effect.Definition.Type, out var handler)
            ? handler.Execute(request)
            : EffectResult.CreateFailure(
                $"Effect type {request.Effect.Definition.Type} not yet implemented");
    }
}
