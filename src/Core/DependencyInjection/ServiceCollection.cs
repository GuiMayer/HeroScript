using System;
using System.Collections.Generic;

namespace Core.DependencyInjection;

/// <summary>
/// Service lifetime enumeration.
/// </summary>
public enum ServiceLifetime
{
    Singleton,
    Transient
}

/// <summary>
/// Service descriptor that holds registration information.
/// </summary>
internal class ServiceDescriptor
{
    public Type ServiceType { get; }
    public Func<IServiceProvider, object> Factory { get; }
    public ServiceLifetime Lifetime { get; }

    public ServiceDescriptor(Type serviceType, Func<IServiceProvider, object> factory, ServiceLifetime lifetime)
    {
        ServiceType = serviceType;
        Factory = factory;
        Lifetime = lifetime;
    }
}

/// <summary>
/// Simple dependency injection container for Core services.
/// </summary>
public class ServiceCollection : IServiceCollection
{
    private readonly List<ServiceDescriptor> _descriptors = new();

    public IServiceCollection AddSingleton<TService>(Func<IServiceProvider, TService> factory) where TService : class
    {
        _descriptors.Add(new ServiceDescriptor(
            typeof(TService),
            provider => factory(provider),
            ServiceLifetime.Singleton));
        return this;
    }

    public IServiceCollection AddSingleton<TService, TImplementation>() 
        where TService : class 
        where TImplementation : class, TService
    {
        return AddSingleton<TService>(provider => 
            (TService)Activator.CreateInstance(typeof(TImplementation))!);
    }

    public IServiceCollection AddTransient<TService>(Func<IServiceProvider, TService> factory) where TService : class
    {
        _descriptors.Add(new ServiceDescriptor(
            typeof(TService),
            provider => factory(provider),
            ServiceLifetime.Transient));
        return this;
    }

    public IServiceCollection AddTransient<TService, TImplementation>() 
        where TService : class 
        where TImplementation : class, TService
    {
        return AddTransient<TService>(provider => 
            (TService)Activator.CreateInstance(typeof(TImplementation))!);
    }

    public IServiceProvider BuildServiceProvider()
    {
        return new ServiceProvider(_descriptors);
    }
}

/// <summary>
/// Service provider implementation that resolves dependencies.
/// </summary>
internal class ServiceProvider : IServiceProvider
{
    private readonly Dictionary<Type, ServiceDescriptor> _descriptors;
    private readonly Dictionary<Type, object> _singletonInstances = new();
    private readonly object _lock = new();

    public ServiceProvider(List<ServiceDescriptor> descriptors)
    {
        _descriptors = new Dictionary<Type, ServiceDescriptor>();
        foreach (var descriptor in descriptors)
        {
            _descriptors[descriptor.ServiceType] = descriptor;
        }
    }

    public TService GetService<TService>() where TService : class
    {
        var service = GetServiceOrNull<TService>();
        if (service == null)
            throw new InvalidOperationException($"Service of type {typeof(TService).Name} is not registered");
        return service;
    }

    public TService? GetServiceOrNull<TService>() where TService : class
    {
        var serviceType = typeof(TService);

        if (!_descriptors.TryGetValue(serviceType, out var descriptor))
            return null;

        if (descriptor.Lifetime == ServiceLifetime.Singleton)
        {
            lock (_lock)
            {
                if (!_singletonInstances.TryGetValue(serviceType, out var instance))
                {
                    instance = descriptor.Factory(this);
                    _singletonInstances[serviceType] = instance;
                }
                return (TService)instance;
            }
        }

        return (TService)descriptor.Factory(this);
    }
}
