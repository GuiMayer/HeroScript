namespace Core.Abstractions.Identifiers;

/// <summary>
/// Strongly-typed identifier for a game entity.
/// </summary>
public readonly record struct EntityId(string Value)
{
    public static EntityId Empty => new(string.Empty);
    public bool IsEmpty => string.IsNullOrEmpty(Value);
    public override string ToString() => Value;
    public static implicit operator string(EntityId id) => id.Value;
    public static implicit operator EntityId(string value) => new(value);
}

/// <summary>
/// Strongly-typed identifier for a run session.
/// </summary>
public readonly record struct RunId(Guid Value)
{
    public static RunId Empty => new(Guid.Empty);
    public bool IsEmpty => Value == Guid.Empty;
    public static RunId NewId() => new(Guid.NewGuid());
    public override string ToString() => Value.ToString();
    public static implicit operator Guid(RunId id) => id.Value;
    public static implicit operator RunId(Guid value) => new(value);
}

/// <summary>
/// Strongly-typed identifier for a combat session.
/// </summary>
public readonly record struct CombatId(Guid Value)
{
    public static CombatId Empty => new(Guid.Empty);
    public bool IsEmpty => Value == Guid.Empty;
    public static CombatId NewId() => new(Guid.NewGuid());
    public override string ToString() => Value.ToString();
    public static implicit operator Guid(CombatId id) => id.Value;
    public static implicit operator CombatId(Guid value) => new(value);
}

/// <summary>
/// Strongly-typed identifier for an actor in combat (may reference an EntityId).
/// </summary>
public readonly record struct ActorId(string Value)
{
    public static ActorId Empty => new(string.Empty);
    public bool IsEmpty => string.IsNullOrEmpty(Value);
    public override string ToString() => Value;
    public static implicit operator string(ActorId id) => id.Value;
    public static implicit operator ActorId(string value) => new(value);
}
