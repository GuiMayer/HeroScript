using System.Collections.Immutable;

namespace Core.Damage;

/// <summary>
/// Contexto imutável de cálculo de dano que flui através do pipeline.
/// Cada bucket processa o contexto e retorna uma nova versão transformada.
/// </summary>
public record DamageContext
{
    private ImmutableHashSet<string> _tags =
        ImmutableHashSet<string>.Empty.WithComparer(StringComparer.Ordinal);
    private ImmutableDictionary<string, float> _modifiers =
        ImmutableDictionary<string, float>.Empty.WithComparers(StringComparer.Ordinal);
    private ImmutableDictionary<string, object> _metadata =
        ImmutableDictionary<string, object>.Empty.WithComparers(StringComparer.Ordinal);
    private ImmutableArray<float> _moreMultipliers = ImmutableArray<float>.Empty;

    /// <summary>
    /// Dano base original (não muda durante o pipeline)
    /// </summary>
    public float BaseDamage { get; init; }
    
    /// <summary>
    /// Dano atual (modificado por cada bucket)
    /// </summary>
    public float CurrentDamage { get; init; }
    
    /// <summary>
    /// Tags da ação (ex: "physical", "spell", "can_crit")
    /// </summary>
    public IReadOnlySet<string> Tags
    {
        get => _tags;
        init => _tags = value?.ToImmutableHashSet(StringComparer.Ordinal)
            ?? ImmutableHashSet<string>.Empty.WithComparer(StringComparer.Ordinal);
    }
    
    /// <summary>
    /// Entradas numéricas nomeadas, incluindo recursos projetados pelos caminhos
    /// canônicos source.resources.* e target.resources.*.
    /// </summary>
    public IReadOnlyDictionary<string, float> Modifiers
    {
        get => _modifiers;
        init => _modifiers = value?.ToImmutableDictionary(StringComparer.Ordinal)
            ?? ImmutableDictionary<string, float>.Empty.WithComparers(StringComparer.Ordinal);
    }
    
    /// <summary>
    /// Metadata adicional (ex: "crit_tier" = 2, "attacker_id" = "hero-1")
    /// </summary>
    public IReadOnlyDictionary<string, object> Metadata
    {
        get => _metadata;
        init => _metadata = value?.ToImmutableDictionary(StringComparer.Ordinal)
            ?? ImmutableDictionary<string, object>.Empty.WithComparers(StringComparer.Ordinal);
    }
    
    /// <summary>
    /// Lista de multiplicadores "more" aplicados sequencialmente.
    /// Cada multiplicador é aplicado separadamente (não somados).
    /// </summary>
    public IReadOnlyList<float> MoreMultipliers
    {
        get => _moreMultipliers;
        init => _moreMultipliers = value?.ToImmutableArray() ?? ImmutableArray<float>.Empty;
    }
    
    /// <summary>
    /// Cria novo contexto com dano atualizado
    /// </summary>
    public DamageContext WithDamage(float newDamage)
    {
        return this with { CurrentDamage = newDamage };
    }
    
    /// <summary>
    /// Cria novo contexto com modifier adicionado/atualizado
    /// </summary>
    public DamageContext WithModifier(string key, float value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        return this with { Modifiers = _modifiers.SetItem(key, value) };
    }

    /// <summary>
    /// Cria novo contexto sem o modificador informado.
    /// </summary>
    public DamageContext WithoutModifier(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        return this with { Modifiers = _modifiers.Remove(key) };
    }
    
    /// <summary>
    /// Cria novo contexto com tag adicionada
    /// </summary>
    public DamageContext WithTag(string tag)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tag);
        return this with { Tags = _tags.Add(tag) };
    }
    
    /// <summary>
    /// Cria novo contexto com tag removida
    /// </summary>
    public DamageContext RemoveTag(string tag)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tag);
        return this with { Tags = _tags.Remove(tag) };
    }
    
    /// <summary>
    /// Cria novo contexto com metadata adicionada/atualizada
    /// </summary>
    public DamageContext WithMetadata(string key, object value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        return this with { Metadata = _metadata.SetItem(key, value) };
    }

    /// <summary>
    /// Cria novo contexto sem a metadata informada.
    /// </summary>
    public DamageContext WithoutMetadata(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        return this with { Metadata = _metadata.Remove(key) };
    }
    
    /// <summary>
    /// Cria novo contexto com multiplicador "more" adicionado à lista
    /// </summary>
    public DamageContext WithMoreMultiplier(float multiplier)
    {
        return this with { MoreMultipliers = _moreMultipliers.Add(multiplier) };
    }
}
