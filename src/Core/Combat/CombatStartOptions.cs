namespace Core.Combat;

/// <summary>
/// Entradas externas que definem uma simulação de combate reproduzível.
/// A fronteira pode omitir Seed; o valor escolhido será persistido no estado.
/// </summary>
public sealed record CombatStartOptions(
    ulong? Seed = null,
    string ContentRevision = "combat-default");
