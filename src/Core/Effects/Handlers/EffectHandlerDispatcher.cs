using System.Collections.Immutable;

namespace Core.Effects.Handlers;

public sealed class EffectHandlerDispatcher
{
    private readonly ImmutableDictionary<(EffectScope Scope, EffectType Type), IEffectHandler> _handlers;

    public EffectHandlerDispatcher(IEnumerable<IEffectHandler> handlers)
    {
        var builder = ImmutableDictionary.CreateBuilder<(EffectScope, EffectType), IEffectHandler>();
        foreach (var handler in handlers)
        {
            foreach (var scope in handler.SupportedScopes)
            foreach (var type in handler.SupportedTypes)
            {
                var key = (scope, type);
                if (builder.ContainsKey(key))
                    throw new InvalidOperationException($"Multiple handlers registered for {scope}/{type}");
                builder[key] = handler;
            }
        }
        _handlers = builder.ToImmutable();
    }

    public EffectResult Execute(EffectExecutionRequest request)
    {
        return _handlers.TryGetValue((request.Context.Scope, request.Effect.Definition.Type), out var handler)
            ? handler.Execute(request)
            : EffectResult.CreateFailure(
                $"Effect type {request.Effect.Definition.Type} is not implemented for {request.Context.Scope}");
    }
}
