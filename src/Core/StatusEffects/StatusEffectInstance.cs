using System.Collections.Immutable;

namespace Core.StatusEffects;

/// <summary>
/// Instância ativa de um status effect aplicado a uma entidade.
/// Representa um status effect específico com seus valores atuais (stacks, duração, etc.).
/// </summary>
public record StatusEffectInstance
{
    /// <summary>
    /// ID único desta instância
    /// </summary>
    public Guid InstanceId { get; init; } = Guid.Empty;
    
    /// <summary>
    /// ID do status effect (referência à definição)
    /// </summary>
    public string StatusId { get; init; } = string.Empty;
    
    /// <summary>
    /// Definição do status effect (carregada do JSON)
    /// </summary>
    public StatusEffectDefinition Definition { get; init; } = null!;
    
    // ===== ALVO =====
    
    /// <summary>
    /// ID da entidade que possui este status (herói ou inimigo)
    /// </summary>
    public string TargetId { get; init; } = string.Empty;
    
    /// <summary>
    /// ID da entidade que aplicou este status (opcional)
    /// Usado para rastrear origem de efeitos
    /// </summary>
    public string? SourceId { get; init; }

    /// <summary>
    /// Immutable content revision that owns the status definition and formulas.
    /// </summary>
    public string? ContentRevision { get; init; }
    
    // ===== VALORES ATUAIS =====
    
    /// <summary>
    /// Número atual de stacks
    /// </summary>
    public int Stacks { get; init; } = 1;
    public ImmutableArray<Core.Effects.StackPayloadLot> PayloadLots { get; init; } = [];
    
    /// <summary>
    /// Turnos restantes (-1 = permanente)
    /// Decrementado no fim de cada turno
    /// </summary>
    public int Duration { get; init; }
    
    // ===== METADATA =====
    
    /// <summary>
    /// Quando o status foi aplicado
    /// </summary>
    public DateTime AppliedAt { get; init; } = DateTime.UnixEpoch;
    
    /// <summary>
    /// Turno em que o status foi aplicado
    /// </summary>
    public int TurnApplied { get; init; }
    
    /// <summary>
    /// Se o status está ativo (não expirado)
    /// </summary>
    public bool IsActive { get; init; } = true;
    
    /// <summary>
    /// Dados customizados para esta instância específica
    /// Ex: { "damage_absorbed": 50 } para Shield
    /// Ex: { "debuffs_prevented": 2 } para Artifact
    /// </summary>
    private ImmutableDictionary<string, object> _customData =
        ImmutableDictionary<string, object>.Empty.WithComparers(StringComparer.Ordinal);

    public IReadOnlyDictionary<string, object> CustomData
    {
        get => _customData;
        init => _customData = value?.ToImmutableDictionary(StringComparer.Ordinal)
            ?? ImmutableDictionary<string, object>.Empty.WithComparers(StringComparer.Ordinal);
    }
}
