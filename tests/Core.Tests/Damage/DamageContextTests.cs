using System.Collections.Generic;
using Core.Damage;
using Xunit;

namespace Core.Tests.Damage;

/// <summary>
/// Testes unitários para DamageContext.
/// Valida imutabilidade, métodos With*, e manipulação de estado.
/// </summary>
public class DamageContextTests
{
    [Fact]
    public void Constructor_CreatesContextWithCorrectValues()
    {
        // Arrange & Act
        var context = new DamageContext
        {
            BaseDamage = 100f,
            CurrentDamage = 150f,
            Modifiers = new Dictionary<string, float> { { "strength", 10f } },
            Tags = new HashSet<string> { "physical" },
            Metadata = new Dictionary<string, object> { { "source", "test" } }
        };

        // Assert
        Assert.Equal(100f, context.BaseDamage);
        Assert.Equal(150f, context.CurrentDamage);
        Assert.Single(context.Modifiers);
        Assert.Equal(10f, context.Modifiers["strength"]);
        Assert.Single(context.Tags);
        Assert.Contains("physical", context.Tags);
        Assert.Single(context.Metadata);
        Assert.Equal("test", context.Metadata["source"]);
    }

    [Fact]
    public void WithDamage_CreatesNewContextWithUpdatedDamage()
    {
        // Arrange
        var original = new DamageContext
        {
            BaseDamage = 100f,
            CurrentDamage = 100f
        };

        // Act
        var updated = original.WithDamage(150f);

        // Assert
        Assert.Equal(100f, original.CurrentDamage); // Original não muda
        Assert.Equal(150f, updated.CurrentDamage);  // Novo contexto tem novo valor
        Assert.Equal(100f, updated.BaseDamage);     // BaseDamage permanece igual
    }

    [Fact]
    public void WithModifier_AddsNewModifier()
    {
        // Arrange
        var original = new DamageContext
        {
            BaseDamage = 100f,
            CurrentDamage = 100f
        };

        // Act
        var updated = original.WithModifier("strength", 15f);

        // Assert
        Assert.Empty(original.Modifiers);           // Original não muda
        Assert.Single(updated.Modifiers);
        Assert.Equal(15f, updated.Modifiers["strength"]);
    }

    [Fact]
    public void WithModifier_UpdatesExistingModifier()
    {
        // Arrange
        var original = new DamageContext
        {
            BaseDamage = 100f,
            CurrentDamage = 100f,
            Modifiers = new Dictionary<string, float> { { "strength", 10f } }
        };

        // Act
        var updated = original.WithModifier("strength", 20f);

        // Assert
        Assert.Equal(10f, original.Modifiers["strength"]); // Original não muda
        Assert.Equal(20f, updated.Modifiers["strength"]);  // Novo valor
    }

    [Fact]
    public void WithTag_AddsNewTag()
    {
        // Arrange
        var original = new DamageContext
        {
            BaseDamage = 100f,
            CurrentDamage = 100f,
            Tags = new HashSet<string> { "physical" }
        };

        // Act
        var updated = original.WithTag("critical");

        // Assert
        Assert.Single(original.Tags);               // Original não muda
        Assert.Equal(2, updated.Tags.Count);
        Assert.Contains("physical", updated.Tags);
        Assert.Contains("critical", updated.Tags);
    }

    [Fact]
    public void RemoveTag_RemovesExistingTag()
    {
        // Arrange
        var original = new DamageContext
        {
            BaseDamage = 100f,
            CurrentDamage = 100f,
            Tags = new HashSet<string> { "physical", "critical" }
        };

        // Act
        var updated = original.RemoveTag("critical");

        // Assert
        Assert.Equal(2, original.Tags.Count);       // Original não muda
        Assert.Single(updated.Tags);
        Assert.Contains("physical", updated.Tags);
        Assert.DoesNotContain("critical", updated.Tags);
    }

    [Fact]
    public void RemoveTag_WithNonExistentTag_DoesNotThrow()
    {
        // Arrange
        var original = new DamageContext
        {
            BaseDamage = 100f,
            CurrentDamage = 100f,
            Tags = new HashSet<string> { "physical" }
        };

        // Act
        var updated = original.RemoveTag("nonexistent");

        // Assert
        Assert.Single(updated.Tags);
        Assert.Contains("physical", updated.Tags);
    }

    [Fact]
    public void WithMetadata_AddsNewMetadata()
    {
        // Arrange
        var original = new DamageContext
        {
            BaseDamage = 100f,
            CurrentDamage = 100f
        };

        // Act
        var updated = original.WithMetadata("crit_tier", 2);

        // Assert
        Assert.Empty(original.Metadata);            // Original não muda
        Assert.Single(updated.Metadata);
        Assert.Equal(2, updated.Metadata["crit_tier"]);
    }

    [Fact]
    public void WithMetadata_UpdatesExistingMetadata()
    {
        // Arrange
        var original = new DamageContext
        {
            BaseDamage = 100f,
            CurrentDamage = 100f,
            Metadata = new Dictionary<string, object> { { "crit_tier", 1 } }
        };

        // Act
        var updated = original.WithMetadata("crit_tier", 3);

        // Assert
        Assert.Equal(1, original.Metadata["crit_tier"]); // Original não muda
        Assert.Equal(3, updated.Metadata["crit_tier"]);  // Novo valor
    }

    [Fact]
    public void Immutability_MultipleOperations_DoNotAffectOriginal()
    {
        // Arrange
        var original = new DamageContext
        {
            BaseDamage = 100f,
            CurrentDamage = 100f,
            Tags = new HashSet<string> { "physical" },
            Modifiers = new Dictionary<string, float> { { "strength", 10f } }
        };

        // Act
        var step1 = original.WithDamage(150f);
        var step2 = step1.WithTag("critical");
        var step3 = step2.WithModifier("strength", 20f);

        // Assert
        Assert.Equal(100f, original.CurrentDamage);
        Assert.Single(original.Tags);
        Assert.Equal(10f, original.Modifiers["strength"]);
        
        Assert.Equal(150f, step3.CurrentDamage);
        Assert.Equal(2, step3.Tags.Count);
        Assert.Equal(20f, step3.Modifiers["strength"]);
    }
}
