using Core.Logging;
using Core.StatusEffects;

namespace Core.Effects.Handlers;

/// <summary>
/// Adaptador para o subsistema legado de status. A mutação fica isolada aqui até
/// que StatusState passe a integrar o agregado imutável de combate.
/// </summary>
public sealed class StatusEffectHandler : IEffectHandler
{
    private static readonly IReadOnlySet<EffectType> Types = new HashSet<EffectType>
    {
        EffectType.APPLY_STATUS,
        EffectType.REMOVE_STATUS,
        EffectType.DISPEL_STATUS
    };

    private readonly IStatusEffectManager? _statusEffects;
    private readonly ILogger _logger;

    public StatusEffectHandler(IStatusEffectManager? statusEffects, ILogger logger)
    {
        _statusEffects = statusEffects;
        _logger = logger;
    }

    public IReadOnlySet<EffectType> SupportedTypes => Types;

    public EffectResult Execute(EffectExecutionRequest request) =>
        request.Effect.Definition.Type switch
        {
            EffectType.APPLY_STATUS => Apply(request),
            EffectType.REMOVE_STATUS => Remove(request),
            EffectType.DISPEL_STATUS => Dispel(request),
            _ => EffectResult.CreateFailure("Unsupported status effect")
        };

    private EffectResult Apply(EffectExecutionRequest request)
    {
        var statusId = request.Effect.Definition.StatusId;
        if (string.IsNullOrWhiteSpace(statusId))
            return EffectResult.CreateFailure("StatusId is required for APPLY_STATUS effect");
        if (!TryTarget(request.TargetId, out var targetId, out var error))
            return error!;

        if (_statusEffects != null)
        {
            Guid? sourceId = Guid.TryParse(request.Effect.SourceEntityId, out var source) ? source : null;
            var applied = _statusEffects.ApplyStatus(
                targetId,
                statusId,
                request.Effect.Definition.StatusStacks ?? 1,
                request.Effect.Definition.StatusDuration,
                sourceId);
            if (applied.IsFailure)
                return EffectResult.CreateFailure(applied.Error ?? "Failed to apply status");
        }
        else
        {
            _logger.LogWarning("StatusEffectManager not available, status effect not applied");
        }

        return EffectResult.CreateSuccess() with
        {
            AffectedEntityIds = new List<string> { request.TargetId },
            StatusApplied = new List<string> { statusId }
        };
    }

    private EffectResult Remove(EffectExecutionRequest request)
    {
        var statusId = request.Effect.Definition.StatusId;
        if (string.IsNullOrWhiteSpace(statusId))
            return EffectResult.CreateFailure("StatusId is required for REMOVE_STATUS effect");
        if (!TryTarget(request.TargetId, out var targetId, out var error))
            return error!;

        if (_statusEffects != null)
        {
            var removed = _statusEffects.RemoveStatusByStatusId(targetId, statusId);
            if (removed.IsFailure)
                return EffectResult.CreateFailure(removed.Error ?? "Failed to remove status");
        }
        else
        {
            _logger.LogWarning("StatusEffectManager not available, status effect not removed");
        }

        return EffectResult.CreateSuccess() with
        {
            AffectedEntityIds = new List<string> { request.TargetId },
            StatusRemoved = new List<string> { statusId }
        };
    }

    private EffectResult Dispel(EffectExecutionRequest request)
    {
        if (!TryTarget(request.TargetId, out var targetId, out var error))
            return error!;

        if (_statusEffects != null)
        {
            var removed = _statusEffects.RemoveAllStatus(targetId);
            if (removed.IsFailure)
                return EffectResult.CreateFailure(removed.Error ?? "Failed to dispel status effects");
        }
        else
        {
            _logger.LogWarning("StatusEffectManager not available, dispel not applied");
        }

        return EffectResult.CreateSuccess() with
        {
            AffectedEntityIds = new List<string> { request.TargetId },
            StatusRemoved = new List<string> { "*" }
        };
    }

    private static bool TryTarget(string value, out Guid targetId, out EffectResult? error)
    {
        if (Guid.TryParse(value, out targetId))
        {
            error = null;
            return true;
        }

        error = EffectResult.CreateFailure($"Invalid target ID format: {value}");
        return false;
    }
}
