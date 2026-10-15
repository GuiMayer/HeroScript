namespace Core.Abstractions.Persistence;

/// <summary>Cheap I/O availability probe, not a validation or inventory of saved runs.</summary>
public interface IRunStoreReadiness
{
    Task ProbeAsync(CancellationToken cancellationToken = default);
}
