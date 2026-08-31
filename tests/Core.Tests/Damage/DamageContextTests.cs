using Core.Damage;
using Xunit;

namespace Core.Tests.Damage;

[Trait("Category", "Unit")]
public sealed class DamageContextTests
{
    [Fact]
    public void Defaults_AreEmptyAndReadable()
    {
        var context = new DamageContext { BaseDamage = 100f, CurrentDamage = 100f };

        Assert.Equal(100f, context.BaseDamage);
        Assert.Empty(context.Modifiers);
        Assert.Empty(context.Tags);
        Assert.Empty(context.Metadata);
        Assert.Empty(context.MoreMultipliers);
    }

    [Fact]
    public void Constructor_DefensivelyCopiesCollections()
    {
        var modifiers = new Dictionary<string, float> { ["damage"] = 10f };
        var tags = new HashSet<string> { "physical" };
        var metadata = new Dictionary<string, object> { ["source"] = "card" };
        var multipliers = new List<float> { 1.5f };
        var context = new DamageContext
        {
            Modifiers = modifiers,
            Tags = tags,
            Metadata = metadata,
            MoreMultipliers = multipliers
        };

        modifiers["damage"] = 99f;
        tags.Add("mutated");
        metadata["source"] = "mutated";
        multipliers.Add(2f);

        Assert.Equal(10f, context.Modifiers["damage"]);
        Assert.DoesNotContain("mutated", context.Tags);
        Assert.Equal("card", context.Metadata["source"]);
        Assert.Single(context.MoreMultipliers);
    }

    [Fact]
    public void WithDamage_ReturnsNewContext()
    {
        var original = new DamageContext { BaseDamage = 100f, CurrentDamage = 100f };

        var changed = original.WithDamage(150f);

        Assert.Equal(100f, original.CurrentDamage);
        Assert.Equal(150f, changed.CurrentDamage);
    }

    [Fact]
    public void WithModifier_DoesNotMutatePreviousContext()
    {
        var original = new DamageContext
        {
            Modifiers = new Dictionary<string, float> { ["damage"] = 10f }
        };

        var changed = original.WithModifier("damage", 20f).WithModifier("armor", 5f);
        var removed = changed.WithoutModifier("damage");

        Assert.Equal(10f, original.Modifiers["damage"]);
        Assert.Equal(20f, changed.Modifiers["damage"]);
        Assert.Equal(5f, changed.Modifiers["armor"]);
        Assert.DoesNotContain("damage", removed.Modifiers);
    }

    [Fact]
    public void TagOperations_PreserveCaseAndPreviousContext()
    {
        var original = new DamageContext { Tags = new HashSet<string> { "Fire" } };

        var changed = original.WithTag("fire").WithTag("FIRE");
        var removed = changed.RemoveTag("fire");

        Assert.Single(original.Tags);
        Assert.Equal(3, changed.Tags.Count);
        Assert.Equal(2, removed.Tags.Count);
        Assert.DoesNotContain("fire", removed.Tags);
    }

    [Fact]
    public void MetadataOperations_DoNotMutatePreviousContext()
    {
        var original = new DamageContext();

        var changed = original
            .WithMetadata("int", 42)
            .WithMetadata("text", "value");
        var removed = changed.WithoutMetadata("int");

        Assert.Empty(original.Metadata);
        Assert.Equal(42, changed.Metadata["int"]);
        Assert.Equal("value", changed.Metadata["text"]);
        Assert.DoesNotContain("int", removed.Metadata);
    }

    [Fact]
    public void MoreMultipliers_AreOrderedAndAllowDuplicates()
    {
        var original = new DamageContext();

        var changed = original
            .WithMoreMultiplier(1.5f)
            .WithMoreMultiplier(2f)
            .WithMoreMultiplier(1.5f);

        Assert.Empty(original.MoreMultipliers);
        Assert.Equal(new[] { 1.5f, 2f, 1.5f }, changed.MoreMultipliers);
    }

    [Fact]
    public void WithExpression_DoesNotExposeMutableCollections()
    {
        var external = new Dictionary<string, float> { ["strength"] = 20f };
        var original = new DamageContext { Modifiers = external };

        var changed = original with { CurrentDamage = 200f };
        external["strength"] = 999f;

        Assert.Equal(20f, original.Modifiers["strength"]);
        Assert.Equal(20f, changed.Modifiers["strength"]);
        Assert.Equal(200f, changed.CurrentDamage);
    }
}
