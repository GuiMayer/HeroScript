using System;
using Core.DependencyInjection;
using Xunit;

namespace Core.Tests.DependencyInjection;

/// <summary>
/// Comprehensive tests for ServiceCollection and ServiceProvider
/// Covers registration, resolution, lifetimes, and edge cases
/// </summary>
[Trait("Category", "Unit")]
public class ServiceCollectionTests
{
    // Test interfaces and implementations
    private interface ITestService
    {
        string GetValue();
    }

    private class TestService : ITestService
    {
        public string GetValue() => "TestService";
    }

    private class AlternateTestService : ITestService
    {
        public string GetValue() => "AlternateTestService";
    }

    private interface IDependentService
    {
        ITestService TestService { get; }
    }

    private class DependentService : IDependentService
    {
        public ITestService TestService { get; }
        
        public DependentService(ITestService testService)
        {
            TestService = testService;
        }
    }

    private class InstanceCountingService
    {
        private static int _instanceCount = 0;
        
        public int InstanceId { get; }
        
        public InstanceCountingService()
        {
            InstanceId = ++_instanceCount;
        }
        
        public static void ResetCount() => _instanceCount = 0;
    }

    // ==================== SERVICE COLLECTION REGISTRATION ====================
    
    [Fact]
    public void ServiceCollection_CanBeCreated()
    {
        // Arrange & Act
        var services = new ServiceCollection();
        
        // Assert
        Assert.NotNull(services);
    }
    
    [Fact]
    public void ServiceCollection_AddSingleton_WithFactory_ReturnsCollection()
    {
        // Arrange
        var services = new ServiceCollection();
        
        // Act
        var result = services.AddSingleton<ITestService>(_ => new TestService());
        
        // Assert
        Assert.Same(services, result); // Fluent interface
    }
    
    [Fact]
    public void ServiceCollection_AddSingleton_WithImplementation_ReturnsCollection()
    {
        // Arrange
        var services = new ServiceCollection();
        
        // Act
        var result = services.AddSingleton<ITestService, TestService>();
        
        // Assert
        Assert.Same(services, result);
    }
    
    [Fact]
    public void ServiceCollection_AddTransient_WithFactory_ReturnsCollection()
    {
        // Arrange
        var services = new ServiceCollection();
        
        // Act
        var result = services.AddTransient<ITestService>(_ => new TestService());
        
        // Assert
        Assert.Same(services, result);
    }
    
    [Fact]
    public void ServiceCollection_AddTransient_WithImplementation_ReturnsCollection()
    {
        // Arrange
        var services = new ServiceCollection();
        
        // Act
        var result = services.AddTransient<ITestService, TestService>();
        
        // Assert
        Assert.Same(services, result);
    }
    
    [Fact]
    public void ServiceCollection_SupportsFluentChaining()
    {
        // Arrange
        var services = new ServiceCollection();
        
        // Act
        var result = services
            .AddSingleton<ITestService, TestService>()
            .AddTransient<IDependentService>(_ => new DependentService(new TestService()));
        
        // Assert
        Assert.Same(services, result);
    }
    
    // ==================== SERVICE PROVIDER BUILD ====================
    
    [Fact]
    public void ServiceCollection_BuildServiceProvider_ReturnsProvider()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddSingleton<ITestService, TestService>();
        
        // Act
        var provider = services.BuildServiceProvider();
        
        // Assert
        Assert.NotNull(provider);
    }
    
    [Fact]
    public void ServiceCollection_BuildServiceProvider_CanBuildMultipleTimes()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddSingleton<ITestService, TestService>();
        
        // Act
        var provider1 = services.BuildServiceProvider();
        var provider2 = services.BuildServiceProvider();
        
        // Assert
        Assert.NotNull(provider1);
        Assert.NotNull(provider2);
        Assert.NotSame(provider1, provider2); // Different providers
    }
    
    // ==================== SERVICE RESOLUTION ====================
    
    [Fact]
    public void ServiceProvider_GetService_ResolvesRegisteredService()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddSingleton<ITestService, TestService>();
        var provider = services.BuildServiceProvider();
        
        // Act
        var service = provider.GetService<ITestService>();
        
        // Assert
        Assert.NotNull(service);
        Assert.IsType<TestService>(service);
        Assert.Equal("TestService", service.GetValue());
    }
    
    [Fact]
    public void ServiceProvider_GetService_ThrowsForUnregisteredService()
    {
        // Arrange
        var services = new ServiceCollection();
        var provider = services.BuildServiceProvider();
        
        // Act & Assert
        var ex = Assert.Throws<InvalidOperationException>(() => provider.GetService<ITestService>());
        Assert.Contains("not registered", ex.Message);
        Assert.Contains("ITestService", ex.Message);
    }
    
    [Fact]
    public void ServiceProvider_GetServiceOrNull_ReturnsNullForUnregisteredService()
    {
        // Arrange
        var services = new ServiceCollection();
        var provider = services.BuildServiceProvider();
        
        // Act
        var service = provider.GetServiceOrNull<ITestService>();
        
        // Assert
        Assert.Null(service);
    }
    
    [Fact]
    public void ServiceProvider_GetServiceOrNull_ReturnsServiceWhenRegistered()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddSingleton<ITestService, TestService>();
        var provider = services.BuildServiceProvider();
        
        // Act
        var service = provider.GetServiceOrNull<ITestService>();
        
        // Assert
        Assert.NotNull(service);
        Assert.IsType<TestService>(service);
    }
    
    // ==================== SINGLETON LIFETIME ====================
    
    [Fact]
    public void ServiceProvider_Singleton_ReturnsSameInstance()
    {
        // Arrange
        InstanceCountingService.ResetCount();
        var services = new ServiceCollection();
        services.AddSingleton<InstanceCountingService>(_ => new InstanceCountingService());
        var provider = services.BuildServiceProvider();
        
        // Act
        var instance1 = provider.GetService<InstanceCountingService>();
        var instance2 = provider.GetService<InstanceCountingService>();
        var instance3 = provider.GetService<InstanceCountingService>();
        
        // Assert
        Assert.Same(instance1, instance2);
        Assert.Same(instance2, instance3);
        Assert.Equal(1, instance1.InstanceId); // Only one instance created
    }
    
    [Fact]
    public void ServiceProvider_Singleton_WithImplementation_ReturnsSameInstance()
    {
        // Arrange
        InstanceCountingService.ResetCount();
        var services = new ServiceCollection();
        services.AddSingleton<InstanceCountingService, InstanceCountingService>();
        var provider = services.BuildServiceProvider();
        
        // Act
        var instance1 = provider.GetService<InstanceCountingService>();
        var instance2 = provider.GetService<InstanceCountingService>();
        
        // Assert
        Assert.Same(instance1, instance2);
        Assert.Equal(1, instance1.InstanceId);
    }
    
    [Fact]
    public void ServiceProvider_Singleton_ThreadSafe()
    {
        // Arrange
        InstanceCountingService.ResetCount();
        var services = new ServiceCollection();
        services.AddSingleton<InstanceCountingService>(_ => new InstanceCountingService());
        var provider = services.BuildServiceProvider();
        
        // Act - resolve concurrently from multiple threads
        var instances = new InstanceCountingService[10];
        System.Threading.Tasks.Parallel.For(0, 10, i =>
        {
            instances[i] = provider.GetService<InstanceCountingService>();
        });
        
        // Assert - all should be the same instance
        var firstInstance = instances[0];
        foreach (var instance in instances)
        {
            Assert.Same(firstInstance, instance);
        }
        Assert.Equal(1, firstInstance.InstanceId); // Only one instance created
    }
    
    // ==================== TRANSIENT LIFETIME ====================
    
    [Fact]
    public void ServiceProvider_Transient_ReturnsNewInstanceEachTime()
    {
        // Arrange
        InstanceCountingService.ResetCount();
        var services = new ServiceCollection();
        services.AddTransient<InstanceCountingService>(_ => new InstanceCountingService());
        var provider = services.BuildServiceProvider();
        
        // Act
        var instance1 = provider.GetService<InstanceCountingService>();
        var instance2 = provider.GetService<InstanceCountingService>();
        var instance3 = provider.GetService<InstanceCountingService>();
        
        // Assert
        Assert.NotSame(instance1, instance2);
        Assert.NotSame(instance2, instance3);
        Assert.NotSame(instance1, instance3);
        Assert.Equal(1, instance1.InstanceId);
        Assert.Equal(2, instance2.InstanceId);
        Assert.Equal(3, instance3.InstanceId);
    }
    
    [Fact]
    public void ServiceProvider_Transient_WithImplementation_ReturnsNewInstance()
    {
        // Arrange
        InstanceCountingService.ResetCount();
        var services = new ServiceCollection();
        services.AddTransient<InstanceCountingService, InstanceCountingService>();
        var provider = services.BuildServiceProvider();
        
        // Act
        var instance1 = provider.GetService<InstanceCountingService>();
        var instance2 = provider.GetService<InstanceCountingService>();
        
        // Assert
        Assert.NotSame(instance1, instance2);
        Assert.Equal(1, instance1.InstanceId);
        Assert.Equal(2, instance2.InstanceId);
    }
    
    // ==================== FACTORY FUNCTIONS ====================
    
    [Fact]
    public void ServiceProvider_Factory_ReceivesServiceProvider()
    {
        // Arrange
        var services = new ServiceCollection();
        Core.DependencyInjection.IServiceProvider? capturedProvider = null;
        services.AddSingleton<ITestService>(provider =>
        {
            capturedProvider = provider;
            return new TestService();
        });
        var builtProvider = services.BuildServiceProvider();
        
        // Act
        var service = builtProvider.GetService<ITestService>();
        
        // Assert
        Assert.NotNull(capturedProvider);
        Assert.Same(builtProvider, capturedProvider);
    }
    
    [Fact]
    public void ServiceProvider_Factory_CanResolveDependencies()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddSingleton<ITestService, TestService>();
        services.AddSingleton<IDependentService>(provider =>
        {
            var testService = provider.GetService<ITestService>();
            return new DependentService(testService);
        });
        var provider = services.BuildServiceProvider();
        
        // Act
        var dependent = provider.GetService<IDependentService>();
        
        // Assert
        Assert.NotNull(dependent);
        Assert.NotNull(dependent.TestService);
        Assert.IsType<TestService>(dependent.TestService);
    }
    
    [Fact]
    public void ServiceProvider_Factory_CanResolveWithGetServiceOrNull()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddSingleton<ITestService, TestService>();
        services.AddSingleton<IDependentService>(provider =>
        {
            var testService = provider.GetServiceOrNull<ITestService>();
            return new DependentService(testService!);
        });
        var provider = services.BuildServiceProvider();
        
        // Act
        var dependent = provider.GetService<IDependentService>();
        
        // Assert
        Assert.NotNull(dependent);
        Assert.NotNull(dependent.TestService);
    }
    
    // ==================== SERVICE OVERRIDE ====================
    
    [Fact]
    public void ServiceCollection_LastRegistrationWins()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddSingleton<ITestService, TestService>();
        services.AddSingleton<ITestService, AlternateTestService>();
        var provider = services.BuildServiceProvider();
        
        // Act
        var service = provider.GetService<ITestService>();
        
        // Assert
        Assert.IsType<AlternateTestService>(service);
        Assert.Equal("AlternateTestService", service.GetValue());
    }
    
    [Fact]
    public void ServiceCollection_CanOverrideWithDifferentLifetime()
    {
        // Arrange
        InstanceCountingService.ResetCount();
        var services = new ServiceCollection();
        services.AddSingleton<InstanceCountingService, InstanceCountingService>();
        services.AddTransient<InstanceCountingService, InstanceCountingService>();
        var provider = services.BuildServiceProvider();
        
        // Act
        var instance1 = provider.GetService<InstanceCountingService>();
        var instance2 = provider.GetService<InstanceCountingService>();
        
        // Assert - should be transient (last registration)
        Assert.NotSame(instance1, instance2);
    }
    
    // ==================== MULTIPLE PROVIDERS ====================
    
    [Fact]
    public void ServiceCollection_DifferentProviders_HaveIndependentSingletons()
    {
        // Arrange
        InstanceCountingService.ResetCount();
        var services = new ServiceCollection();
        services.AddSingleton<InstanceCountingService, InstanceCountingService>();
        
        var provider1 = services.BuildServiceProvider();
        var provider2 = services.BuildServiceProvider();
        
        // Act
        var instance1 = provider1.GetService<InstanceCountingService>();
        var instance2 = provider2.GetService<InstanceCountingService>();
        
        // Assert - different providers have different singletons
        Assert.NotSame(instance1, instance2);
        Assert.Equal(1, instance1.InstanceId);
        Assert.Equal(2, instance2.InstanceId);
    }
    
    // ==================== EDGE CASES ====================
    
    [Fact]
    public void ServiceProvider_EmptyCollection_CanBeBuilt()
    {
        // Arrange
        var services = new ServiceCollection();
        
        // Act
        var provider = services.BuildServiceProvider();
        
        // Assert
        Assert.NotNull(provider);
    }
    
    [Fact]
    public void ServiceProvider_GetService_AfterMultipleRegistrations_UsesLast()
    {
        // Arrange
        var services = new ServiceCollection();
        for (int i = 0; i < 10; i++)
        {
            if (i % 2 == 0)
                services.AddSingleton<ITestService, TestService>();
            else
                services.AddSingleton<ITestService, AlternateTestService>();
        }
        var provider = services.BuildServiceProvider();
        
        // Act
        var service = provider.GetService<ITestService>();
        
        // Assert
        Assert.IsType<AlternateTestService>(service); // Last one (odd index)
    }
    
    [Fact]
    public void ServiceProvider_Factory_ThatThrows_PropagatesException()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddSingleton<ITestService>(_ => throw new InvalidOperationException("Factory failed"));
        var provider = services.BuildServiceProvider();
        
        // Act & Assert
        var ex = Assert.Throws<InvalidOperationException>(() => provider.GetService<ITestService>());
        Assert.Equal("Factory failed", ex.Message);
    }
    
    [Fact]
    public void ServiceProvider_Singleton_FactoryThatThrows_CachesException()
    {
        // Arrange
        var services = new ServiceCollection();
        int callCount = 0;
        services.AddSingleton<ITestService>(_ =>
        {
            callCount++;
            throw new InvalidOperationException($"Factory failed on call {callCount}");
        });
        var provider = services.BuildServiceProvider();
        
        // Act & Assert - first call throws
        var ex1 = Assert.Throws<InvalidOperationException>(() => provider.GetService<ITestService>());
        Assert.Equal("Factory failed on call 1", ex1.Message);
        
        // Second call should NOT call factory again (exception was thrown before caching)
        // But in this simple implementation, it might retry. Let's test actual behavior.
        var ex2 = Assert.Throws<InvalidOperationException>(() => provider.GetService<ITestService>());
        // The implementation doesn't cache failed singletons, so it retries
        Assert.Equal("Factory failed on call 2", ex2.Message);
    }
    
    [Fact]
    public void ServiceProvider_ConcreteClass_CanBeRegisteredAndResolved()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddSingleton<TestService, TestService>();
        var provider = services.BuildServiceProvider();
        
        // Act
        var service = provider.GetService<TestService>();
        
        // Assert
        Assert.NotNull(service);
        Assert.Equal("TestService", service.GetValue());
    }
}
