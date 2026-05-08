using System;

namespace Core.DependencyInjection;

/// <summary>
/// Interface for service collection that registers dependencies.
/// </summary>
public interface IServiceCollection
{
    /// <summary>
    /// Registers a singleton service with a factory function.
    /// </summary>
    IServiceCollection AddSingleton<TService>(Func<IServiceProvider, TService> factory) where TService : class;

    /// <summary>
    /// Registers a singleton service with a concrete implementation.
    /// </summary>
    IServiceCollection AddSingleton<TService, TImplementation>() 
        where TService : class 
        where TImplementation : class, TService;

    /// <summary>
    /// Registers a transient service with a factory function.
    /// </summary>
    IServiceCollection AddTransient<TService>(Func<IServiceProvider, TService> factory) where TService : class;

    /// <summary>
    /// Registers a transient service with a concrete implementation.
    /// </summary>
    IServiceCollection AddTransient<TService, TImplementation>() 
        where TService : class 
        where TImplementation : class, TService;

    /// <summary>
    /// Builds the service provider from registered services.
    /// </summary>
    IServiceProvider BuildServiceProvider();
}
