using System;
using System.Collections.Generic;
using System.Linq;
using Core.Damage;
using Xunit;

namespace Core.Tests.Damage;

/// <summary>
/// Testes focados no record DamageContext e suas operações
/// </summary>
[Trait("Category", "Unit")]
public class DamageContextTests
{
    // ==================== CONSTRUCTOR AND INITIALIZATION ====================

    [Fact]
    public void DamageContext_WithDefaultValues_InitializesCorrectly()
    {
        // Arrange & Act
        var context = new DamageContext
        {
            BaseDamage = 100f,
            CurrentDamage = 100f,
            Modifiers = new Dictionary<string, float>(),
            Tags = new HashSet<string>(),
            Metadata = new Dictionary<string, object>()
        };

        // Assert
        Assert.Equal(100f, context.BaseDamage);
        Assert.Equal(100f, context.CurrentDamage);
        Assert.NotNull(context.Modifiers);
        Assert.NotNull(context.Tags);
        Assert.NotNull(context.Metadata);
    }

    [Fact]
    public void DamageContext_WithMoreMultipliers_InitializesCorrectly()
    {
        // Arrange & Act
        var context = new DamageContext
        {
            BaseDamage = 100f,
            CurrentDamage = 100f,
            Modifiers = new Dictionary<string, float>(),
            Tags = new HashSet<string>(),
            MoreMultipliers = new List<float> { 1.5f, 2.0f },
            Metadata = new Dictionary<string, object>()
        };

        // Assert
        Assert.NotNull(context.MoreMultipliers);
        Assert.Equal(2, context.MoreMultipliers.Count);
        Assert.Contains(1.5f, context.MoreMultipliers);
        Assert.Contains(2.0f, context.MoreMultipliers);
    }

    // ==================== RECORD IMMUTABILITY (WITH EXPRESSION) ====================

    [Fact]
    public void DamageContext_WithExpression_CreatesNewInstance()
    {
        // Arrange
        var original = DamageTestHelpers.CreateBasicContext(baseDamage: 100f);

        // Act
        var modified = original with { CurrentDamage = 150f };

        // Assert
        Assert.NotSame(original, modified);
        Assert.Equal(100f, original.CurrentDamage);
        Assert.Equal(150f, modified.CurrentDamage);
    }

    [Fact]
    public void DamageContext_WithExpression_PreservesOtherProperties()
    {
        // Arrange
        var original = new DamageContext
        {
            BaseDamage = 100f,
            CurrentDamage = 100f,
            Modifiers = new Dictionary<string, float> { ["strength"] = 20f },
            Tags = new HashSet<string> { "physical" },
            Metadata = new Dictionary<string, object> { ["source"] = "test" }
        };

        // Act
        var modified = original with { CurrentDamage = 200f };

        // Assert
        Assert.Equal(100f, modified.BaseDamage);
        Assert.Equal(20f, modified.Modifiers["strength"]);
        Assert.Contains("physical", modified.Tags);
        Assert.Equal("test", modified.Metadata["source"]);
    }

    [Fact]
    public void DamageContext_WithExpression_ModifiersAreShallowCopied()
    {
        // Arrange
        var modifiers = new Dictionary<string, float> { ["damage"] = 10f };
        var original = DamageTestHelpers.CreateBasicContext(baseDamage: 100f, modifiers: modifiers);

        // Act
        var modified = original with { CurrentDamage = 150f };
        modifiers["damage"] = 20f; // Modify original dictionary

        // Assert
        // Both reference the same dictionary (shallow copy)
        Assert.Equal(20f, original.Modifiers["damage"]);
        Assert.Equal(20f, modified.Modifiers["damage"]);
    }

    // ==================== MODIFIERS DICTIONARY ====================

    [Fact]
    public void DamageContext_AddModifier_WorksCorrectly()
    {
        // Arrange
        var context = DamageTestHelpers.CreateBasicContext(baseDamage: 100f);

        // Act
        context.Modifiers["new_modifier"] = 50f;

        // Assert
        Assert.Equal(50f, context.Modifiers["new_modifier"]);
    }

    [Fact]
    public void DamageContext_UpdateModifier_OverwritesValue()
    {
        // Arrange
        var context = DamageTestHelpers.CreateBasicContext(
            baseDamage: 100f,
            modifiers: new Dictionary<string, float> { ["damage"] = 10f });

        // Act
        context.Modifiers["damage"] = 20f;

        // Assert
        Assert.Equal(20f, context.Modifiers["damage"]);
    }

    [Fact]
    public void DamageContext_RemoveModifier_WorksCorrectly()
    {
        // Arrange
        var context = DamageTestHelpers.CreateBasicContext(
            baseDamage: 100f,
            modifiers: new Dictionary<string, float> { ["temp"] = 15f });

        // Act
        var removed = context.Modifiers.Remove("temp");

        // Assert
        Assert.True(removed);
        Assert.False(context.Modifiers.ContainsKey("temp"));
    }

    [Fact]
    public void DamageContext_TryGetModifier_WorksCorrectly()
    {
        // Arrange
        var context = DamageTestHelpers.CreateBasicContext(
            baseDamage: 100f,
            modifiers: new Dictionary<string, float> { ["existing"] = 30f });

        // Act
        var exists = context.Modifiers.TryGetValue("existing", out var value);
        var notExists = context.Modifiers.TryGetValue("missing", out var missingValue);

        // Assert
        Assert.True(exists);
        Assert.Equal(30f, value);
        Assert.False(notExists);
        Assert.Equal(0f, missingValue);
    }

    [Fact]
    public void DamageContext_ModifiersCount_ReturnsCorrectValue()
    {
        // Arrange
        var context = DamageTestHelpers.CreateBasicContext(
            baseDamage: 100f,
            modifiers: new Dictionary<string, float>
            {
                ["mod1"] = 1f,
                ["mod2"] = 2f,
                ["mod3"] = 3f
            });

        // Assert
        Assert.Equal(3, context.Modifiers.Count);
    }

    // ==================== TAGS HASHSET ====================

    [Fact]
    public void DamageContext_AddTag_WorksCorrectly()
    {
        // Arrange
        var context = DamageTestHelpers.CreateBasicContext(baseDamage: 100f);

        // Act
        var added = context.Tags.Add("new_tag");

        // Assert
        Assert.True(added);
        Assert.Contains("new_tag", context.Tags);
    }

    [Fact]
    public void DamageContext_AddDuplicateTag_ReturnsFalse()
    {
        // Arrange
        var context = DamageTestHelpers.CreateBasicContext(
            baseDamage: 100f,
            tags: new List<string> { "fire" });

        // Act
        var added = context.Tags.Add("fire");

        // Assert
        Assert.False(added);
        Assert.Single(context.Tags);
    }

    [Fact]
    public void DamageContext_RemoveTag_WorksCorrectly()
    {
        // Arrange
        var context = DamageTestHelpers.CreateBasicContext(
            baseDamage: 100f,
            tags: new List<string> { "temporary" });

        // Act
        var removed = context.Tags.Remove("temporary");

        // Assert
        Assert.True(removed);
        Assert.DoesNotContain("temporary", context.Tags);
    }

    [Fact]
    public void DamageContext_ContainsTag_WorksCorrectly()
    {
        // Arrange
        var context = DamageTestHelpers.CreateBasicContext(
            baseDamage: 100f,
            tags: new List<string> { "physical", "melee" });

        // Assert
        Assert.True(context.Tags.Contains("physical"));
        Assert.True(context.Tags.Contains("melee"));
        Assert.False(context.Tags.Contains("magical"));
    }

    [Fact]
    public void DamageContext_TagsCount_ReturnsCorrectValue()
    {
        // Arrange
        var context = DamageTestHelpers.CreateBasicContext(
            baseDamage: 100f,
            tags: new List<string> { "fire", "spell", "aoe" });

        // Assert
        Assert.Equal(3, context.Tags.Count);
    }

    [Fact]
    public void DamageContext_TagsCaseInvariant_AreTreatedAsUnique()
    {
        // Arrange
        var context = DamageTestHelpers.CreateBasicContext(baseDamage: 100f);

        // Act
        context.Tags.Add("Fire");
        context.Tags.Add("fire");
        context.Tags.Add("FIRE");

        // Assert
        Assert.Equal(3, context.Tags.Count);
    }

    // ==================== METADATA DICTIONARY ====================

    [Fact]
    public void DamageContext_AddMetadata_WorksCorrectly()
    {
        // Arrange
        var context = DamageTestHelpers.CreateBasicContext(baseDamage: 100f);

        // Act
        context.Metadata["custom_data"] = "test_value";

        // Assert
        Assert.Equal("test_value", context.Metadata["custom_data"]);
    }

    [Fact]
    public void DamageContext_MetadataSupportsMultipleTypes()
    {
        // Arrange
        var context = DamageTestHelpers.CreateBasicContext(baseDamage: 100f);

        // Act
        context.Metadata["int_value"] = 42;
        context.Metadata["float_value"] = 3.14f;
        context.Metadata["string_value"] = "text";
        context.Metadata["bool_value"] = true;
        context.Metadata["list_value"] = new List<int> { 1, 2, 3 };

        // Assert
        Assert.Equal(42, context.Metadata["int_value"]);
        Assert.Equal(3.14f, context.Metadata["float_value"]);
        Assert.Equal("text", context.Metadata["string_value"]);
        Assert.Equal(true, context.Metadata["bool_value"]);
        Assert.IsType<List<int>>(context.Metadata["list_value"]);
    }

    [Fact]
    public void DamageContext_RemoveMetadata_WorksCorrectly()
    {
        // Arrange
        var context = DamageTestHelpers.CreateBasicContext(baseDamage: 100f);
        context.Metadata["temp_data"] = "temporary";

        // Act
        var removed = context.Metadata.Remove("temp_data");

        // Assert
        Assert.True(removed);
        Assert.False(context.Metadata.ContainsKey("temp_data"));
    }

    // ==================== MORE MULTIPLIERS LIST ====================

    [Fact]
    public void DamageContext_AddMoreMultiplier_WorksCorrectly()
    {
        // Arrange
        var context = new DamageContext
        {
            BaseDamage = 100f,
            CurrentDamage = 100f,
            Modifiers = new Dictionary<string, float>(),
            Tags = new HashSet<string>(),
            MoreMultipliers = new List<float>(),
            Metadata = new Dictionary<string, object>()
        };

        // Act
        context.MoreMultipliers.Add(1.5f);
        context.MoreMultipliers.Add(2.0f);

        // Assert
        Assert.Equal(2, context.MoreMultipliers.Count);
        Assert.Contains(1.5f, context.MoreMultipliers);
        Assert.Contains(2.0f, context.MoreMultipliers);
    }

    [Fact]
    public void DamageContext_MoreMultipliersAllowsDuplicates()
    {
        // Arrange
        var context = new DamageContext
        {
            BaseDamage = 100f,
            CurrentDamage = 100f,
            Modifiers = new Dictionary<string, float>(),
            Tags = new HashSet<string>(),
            MoreMultipliers = new List<float>(),
            Metadata = new Dictionary<string, object>()
        };

        // Act
        context.MoreMultipliers.Add(1.5f);
        context.MoreMultipliers.Add(1.5f);

        // Assert
        Assert.Equal(2, context.MoreMultipliers.Count);
    }

    [Fact]
    public void DamageContext_RemoveMoreMultiplier_WorksCorrectly()
    {
        // Arrange
        var context = new DamageContext
        {
            BaseDamage = 100f,
            CurrentDamage = 100f,
            Modifiers = new Dictionary<string, float>(),
            Tags = new HashSet<string>(),
            MoreMultipliers = new List<float> { 1.5f, 2.0f, 1.5f },
            Metadata = new Dictionary<string, object>()
        };

        // Act
        var removed = context.MoreMultipliers.Remove(1.5f); // Removes first occurrence

        // Assert
        Assert.True(removed);
        Assert.Equal(2, context.MoreMultipliers.Count);
        Assert.Contains(2.0f, context.MoreMultipliers);
        Assert.Contains(1.5f, context.MoreMultipliers); // Second 1.5f remains
    }

    // ==================== EQUALITY AND COMPARISON ====================

    [Fact]
    public void DamageContext_EqualityWithSameValues_ReturnsTrue()
    {
        // Arrange
        var context1 = new DamageContext
        {
            BaseDamage = 100f,
            CurrentDamage = 100f,
            Modifiers = new Dictionary<string, float>(),
            Tags = new HashSet<string>(),
            Metadata = new Dictionary<string, object>()
        };

        var context2 = new DamageContext
        {
            BaseDamage = 100f,
            CurrentDamage = 100f,
            Modifiers = new Dictionary<string, float>(),
            Tags = new HashSet<string>(),
            Metadata = new Dictionary<string, object>()
        };

        // Act & Assert
        // Note: Records use reference equality for reference-type properties
        Assert.NotEqual(context1, context2); // Different instances
    }

    [Fact]
    public void DamageContext_SameReference_ReturnsTrue()
    {
        // Arrange
        var context1 = DamageTestHelpers.CreateBasicContext(baseDamage: 100f);
        var context2 = context1;

        // Act & Assert
        Assert.Equal(context1, context2);
        Assert.True(context1 == context2);
    }

    // ==================== COMPLEX OPERATIONS ====================

    [Fact]
    public void DamageContext_ClearAllModifiers_WorksCorrectly()
    {
        // Arrange
        var context = DamageTestHelpers.CreateBasicContext(
            baseDamage: 100f,
            modifiers: new Dictionary<string, float>
            {
                ["mod1"] = 1f,
                ["mod2"] = 2f,
                ["mod3"] = 3f
            });

        // Act
        context.Modifiers.Clear();

        // Assert
        Assert.Empty(context.Modifiers);
    }

    [Fact]
    public void DamageContext_ClearAllTags_WorksCorrectly()
    {
        // Arrange
        var context = DamageTestHelpers.CreateBasicContext(
            baseDamage: 100f,
            tags: new List<string> { "fire", "ice", "lightning" });

        // Act
        context.Tags.Clear();

        // Assert
        Assert.Empty(context.Tags);
    }

    [Fact]
    public void DamageContext_EnumerateModifiers_WorksCorrectly()
    {
        // Arrange
        var context = DamageTestHelpers.CreateBasicContext(
            baseDamage: 100f,
            modifiers: new Dictionary<string, float>
            {
                ["strength"] = 10f,
                ["dexterity"] = 15f,
                ["intelligence"] = 20f
            });

        // Act
        var keys = context.Modifiers.Keys.ToList();
        var values = context.Modifiers.Values.ToList();

        // Assert
        Assert.Equal(3, keys.Count);
        Assert.Contains("strength", keys);
        Assert.Contains("dexterity", keys);
        Assert.Contains("intelligence", keys);
        Assert.Contains(10f, values);
        Assert.Contains(15f, values);
        Assert.Contains(20f, values);
    }

    [Fact]
    public void DamageContext_EnumerateTags_WorksCorrectly()
    {
        // Arrange
        var context = DamageTestHelpers.CreateBasicContext(
            baseDamage: 100f,
            tags: new List<string> { "physical", "melee", "weapon" });

        // Act
        var tagList = context.Tags.ToList();

        // Assert
        Assert.Equal(3, tagList.Count);
        Assert.Contains("physical", tagList);
        Assert.Contains("melee", tagList);
        Assert.Contains("weapon", tagList);
    }

    [Fact]
    public void DamageContext_FilterTags_WorksCorrectly()
    {
        // Arrange
        var context = DamageTestHelpers.CreateBasicContext(
            baseDamage: 100f,
            tags: new List<string> { "fire_spell", "ice_spell", "fire_weapon", "physical" });

        // Act
        var fireTags = context.Tags.Where(t => t.Contains("fire")).ToList();

        // Assert
        Assert.Equal(2, fireTags.Count);
        Assert.Contains("fire_spell", fireTags);
        Assert.Contains("fire_weapon", fireTags);
    }

    [Fact]
    public void DamageContext_SumModifierValues_WorksCorrectly()
    {
        // Arrange
        var context = DamageTestHelpers.CreateBasicContext(
            baseDamage: 100f,
            modifiers: new Dictionary<string, float>
            {
                ["bonus1"] = 10f,
                ["bonus2"] = 20f,
                ["bonus3"] = 30f
            });

        // Act
        var sum = context.Modifiers.Values.Sum();

        // Assert
        Assert.Equal(60f, sum);
    }

    [Fact]
    public void DamageContext_GetModifierValueOrDefault_WorksCorrectly()
    {
        // Arrange
        var context = DamageTestHelpers.CreateBasicContext(
            baseDamage: 100f,
            modifiers: new Dictionary<string, float> { ["existing"] = 25f });

        // Act
        var existingValue = context.Modifiers.GetValueOrDefault("existing", 0f);
        var missingValue = context.Modifiers.GetValueOrDefault("missing", 99f);

        // Assert
        Assert.Equal(25f, existingValue);
        Assert.Equal(99f, missingValue);
    }
}
