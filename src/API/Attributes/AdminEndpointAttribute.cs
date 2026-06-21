namespace API.Attributes;

/// <summary>
/// Marks an endpoint as requiring the configured admin API key.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class AdminEndpointAttribute : Attribute
{
}
