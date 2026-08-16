using Core.Determinism;

namespace Core.Damage;

/// <summary>
/// Cursor local que adapta o PRNG puro da engine a APIs sequenciais. A instância
/// deve existir somente durante uma transição; o chamador persiste Context ao fim.
/// </summary>
public sealed class DeterministicRandomProvider : IRandomProvider
{
    public DeterministicContext Context { get; private set; }

    public DeterministicRandomProvider(DeterministicContext context)
    {
        Context = context ?? throw new ArgumentNullException(nameof(context));
    }

    public double NextDouble()
    {
        var draw = Context.DrawDouble();
        Context = draw.Context;
        return draw.Value;
    }

    public int Next(int maxValue)
    {
        var draw = Context.DrawInt32(maxValue);
        Context = draw.Context;
        return draw.Value;
    }

    public int Next(int minValue, int maxValue)
    {
        if (maxValue <= minValue)
            throw new ArgumentOutOfRangeException(nameof(maxValue), "Maximum must be greater than minimum.");

        return checked(minValue + Next(maxValue - minValue));
    }

    public Guid AllocateId(string scope)
    {
        var allocation = Context.AllocateId(scope);
        Context = allocation.Context;
        return allocation.Value;
    }

    public DateTime LogicalTimestamp => Context.LogicalTimestamp.UtcDateTime;
}
