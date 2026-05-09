using System.Collections.Generic;
using Core.Damage;
using Core.Events;
using Core.Logging;
using Core.Math;
using Moq;

namespace Core.Tests.Damage;

/// <summary>
/// Helpers para criar objetos de teste do sistema de dano
/// </summary>
public static class DamageTestHelpers
{
    // ==================== MOCK FACTORIES ====================
    
    public static Mock<ILogger> CreateMockLogger()
    {
        var mock = new Mock<ILogger>();
        mock.Setup(l => l.LogDebug(It.IsAny<string>()));
        mock.Setup(l => l.LogInformation(It.IsAny<string>()));
        mock.Setup(l => l.LogWarning(It.IsAny<string>()));
        mock.Setup(l => l.LogError(It.IsAny<string>()));
        return mock;
    }
    
    public static Mock<IEventBus> CreateMockEventBus()
    {
        var mock = new Mock<IEventBus>();
        mock.Setup(e => e.Publish(It.IsAny<IEvent>()));
        return mock;
    }
    
    public static Mock<IMathEngine> CreateMockMathEngine()
    {
        var mock = new Mock<IMathEngine>();
        
        // Setup básico: retorna uma expressão matemática simples
        mock.Setup(m => m.BuildFromFormula(
            It.IsAny<string>(), 
            It.IsAny<float>(), 
            It.IsAny<Dictionary<string, float>>()))
            .Returns((string formula, float input, Dictionary<string, float>? paramOverrides) => 
            {
                // Retorna uma expressão que simplesmente retorna o input
                return new MathExpression(input);
            });
        
        return mock;
    }
    
    public static Mock<IRandomProvider> CreateMockRandomProvider(double fixedValue = 0.5)
    {
        var mock = new Mock<IRandomProvider>();
        mock.Setup(r => r.NextDouble()).Returns(fixedValue);
        mock.Setup(r => r.Next(It.IsAny<int>())).Returns((int max) => (int)(fixedValue * max));
        mock.Setup(r => r.Next(It.IsAny<int>(), It.IsAny<int>()))
            .Returns((int min, int max) => min + (int)(fixedValue * (max - min)));
        return mock;
    }
    
    // ==================== FACTORIES ====================
    
    public static DamageContext CreateBasicContext(
        float baseDamage = 100f,
        Dictionary<string, float>? modifiers = null,
        List<string>? tags = null)
    {
        return new DamageContext
        {
            BaseDamage = baseDamage,
            CurrentDamage = baseDamage,
            Modifiers = modifiers ?? new Dictionary<string, float>(),
            Tags = new HashSet<string>(tags ?? new List<string>()),
            Metadata = new Dictionary<string, object>()
        };
    }
    
    public static BucketDefinition CreateSimpleBucket(
        string bucketId,
        List<BucketOperation>? operations = null,
        List<FilterCondition>? filters = null,
        int order = 1)
    {
        return new BucketDefinition
        {
            BucketId = bucketId,
            Order = order,
            Operations = operations ?? new List<BucketOperation>(),
            FilterConditions = filters ?? new List<FilterCondition>()
        };
    }
    
    public static PipelineConfiguration CreateSimplePipeline(
        string configName,
        List<BucketDefinition>? buckets = null)
    {
        return new PipelineConfiguration
        {
            ConfigName = configName,
            Buckets = buckets ?? new List<BucketDefinition>()
        };
    }
    
    // ==================== ASSERTION HELPERS ====================
    
    public static void AssertDamageInRange(float actual, float expected, float tolerance = 0.01f)
    {
        var diff = System.Math.Abs(actual - expected);
        if (diff > tolerance)
        {
            throw new System.Exception($"Expected damage {expected} ± {tolerance}, but got {actual}");
        }
    }
    
    public static void AssertContextHasModifier(DamageContext context, string key, float expectedValue)
    {
        if (!context.Modifiers.ContainsKey(key))
        {
            throw new System.Exception($"Context does not contain modifier '{key}'");
        }
        
        var actual = context.Modifiers[key];
        if (System.Math.Abs(actual - expectedValue) > 0.01f)
        {
            throw new System.Exception($"Expected modifier '{key}' to be {expectedValue}, but got {actual}");
        }
    }
    
    public static void AssertContextHasTag(DamageContext context, string tag)
    {
        if (!context.Tags.Contains(tag))
        {
            throw new System.Exception($"Context does not contain tag '{tag}'");
        }
    }
    
    public static void AssertContextHasMetadata(DamageContext context, string key)
    {
        if (!context.Metadata.ContainsKey(key))
        {
            throw new System.Exception($"Context does not contain metadata '{key}'");
        }
    }
}
