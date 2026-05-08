using System;

namespace Core.DependencyInjection;

/// <summary>
/// Interface for service provider that resolves dependencies.
/// </summary>
public interface IServiceProvider
{
    /// <summary>
    /// Gets a service of the specified type.
    /// </summary>
    /// <typeparam name="TService">The type of service to retrieve</typeparam>
    /// <returns>The service instance</returns>
    /// <exception cref="InvalidOperationException">Thrown if service is not registered</exception>
    TService GetService<TService>() where TService : class;

    /// <summary>
    /// Gets a service of the specified type, or null if not registered.
    /// </summary>
    /// <typeparam name="TService">The type of service to retrieve</typeparam>
    /// <returns>The service instance, or null if not registered</returns>
    TService? GetServiceOrNull<TService>() where TService : class;
}
