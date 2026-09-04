namespace Core.Resources;

/// <summary>
/// Canonical projection of an owner's resources into formula variables. Every
/// gameplay source uses the same names, so a formula is independent of the
/// subsystem that invoked it.
/// </summary>
public static class ResourceFormulaVariables
{
    public static void AddOwner(
        IDictionary<string, float> variables,
        string prefix,
        ResourceSet resources)
    {
        ArgumentNullException.ThrowIfNull(variables);
        ArgumentException.ThrowIfNullOrWhiteSpace(prefix);
        ArgumentNullException.ThrowIfNull(resources);

        Add(variables, $"{prefix}.resources", resources.Resources);
    }

    public static void AddUnscoped(
        IDictionary<string, float> variables,
        ResourceSet resources)
    {
        ArgumentNullException.ThrowIfNull(variables);
        ArgumentNullException.ThrowIfNull(resources);

        AddUnscoped(variables, resources.Resources);
    }

    public static void AddUnscoped(
        IDictionary<string, float> variables,
        IReadOnlyDictionary<string, ResourcePool> resources)
    {
        ArgumentNullException.ThrowIfNull(variables);
        ArgumentNullException.ThrowIfNull(resources);

        Add(variables, "resources", resources);
    }

    private static void Add(
        IDictionary<string, float> variables,
        string prefix,
        IReadOnlyDictionary<string, ResourcePool> resources)
    {
        foreach (var (resourceId, pool) in resources
                     .OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            var path = $"{prefix}.{resourceId}";
            variables[$"{path}.current"] = pool.Current;
            variables[$"{path}.minimum"] = pool.Minimum;
            variables[$"{path}.maximum"] = pool.Maximum;
            variables[$"{path}.percent"] = pool.GetPercentage();
        }
    }
}
