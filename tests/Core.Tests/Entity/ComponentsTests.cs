using System;
using System.Collections.Generic;
using System.Linq;
using Core.Entity.Components;
using Xunit;

namespace Core.Tests.Entity;

/// <summary>
/// Comprehensive tests for Entity Components
/// Covers InventoryComponent, InventoryItem and StatsComponent.
/// </summary>
[Trait("Category", "Unit")]
public class ComponentsTests
{
    // ==================== INVENTORY ITEM TESTS ====================
    
    [Fact]
    public void InventoryItem_DefaultConstruction_SetsDefaults()
    {
        // Arrange & Act
        var item = new InventoryItem();
        
        // Assert
        Assert.Equal(string.Empty, item.ItemId);
        Assert.Equal(string.Empty, item.Name);
        Assert.Equal(1, item.Quantity);
        Assert.False(item.IsEquipped);
        Assert.Empty(item.CustomData);
    }
    
    [Fact]
    public void InventoryItem_FullConstruction_SetsAllProperties()
    {
        // Arrange & Act
        var item = new InventoryItem
        {
            ItemId = "sword_001",
            Name = "Iron Sword",
            Quantity = 1,
            IsEquipped = true,
            CustomData = new Dictionary<string, object> { ["damage"] = 10 }
        };
        
        // Assert
        Assert.Equal("sword_001", item.ItemId);
        Assert.Equal("Iron Sword", item.Name);
        Assert.Equal(1, item.Quantity);
        Assert.True(item.IsEquipped);
        Assert.Single(item.CustomData);
    }
    
    [Fact]
    public void InventoryItem_IsRecord_SupportsWithExpression()
    {
        // Arrange
        var original = new InventoryItem
        {
            ItemId = "potion_001",
            Name = "Health Potion",
            Quantity = 5
        };
        
        // Act
        var modified = original with { Quantity = 3 };
        
        // Assert
        Assert.Equal(5, original.Quantity);
        Assert.Equal(3, modified.Quantity);
        Assert.Equal("potion_001", modified.ItemId);
    }
    
    // ==================== INVENTORY COMPONENT TESTS ====================
    
    [Fact]
    public void InventoryComponent_DefaultConstruction_InitializesEmpty()
    {
        // Arrange & Act
        var inventory = new InventoryComponent();
        
        // Assert
        Assert.Empty(inventory.Items);
        Assert.Equal(-1, inventory.MaxCapacity);
    }
    
    [Fact]
    public void InventoryComponent_ConstructionWithItems_SetsItems()
    {
        // Arrange
        var items = new List<InventoryItem>
        {
            new() { ItemId = "item1", Name = "Item 1" },
            new() { ItemId = "item2", Name = "Item 2" }
        };
        
        // Act
        var inventory = new InventoryComponent(items);
        
        // Assert
        Assert.Equal(2, inventory.Items.Count);
    }
    
    [Fact]
    public void InventoryComponent_ConstructionWithCapacity_SetsMaxCapacity()
    {
        // Arrange & Act
        var inventory = new InventoryComponent(null, 20);
        
        // Assert
        Assert.Equal(20, inventory.MaxCapacity);
    }
    
    [Fact]
    public void InventoryComponent_AddItem_AddsItemToInventory()
    {
        // Arrange
        var inventory = new InventoryComponent();
        var item = new InventoryItem { ItemId = "sword_001", Name = "Iron Sword" };
        
        // Act
        var newInventory = inventory.AddItem(item);
        
        // Assert
        Assert.Single(newInventory.Items);
        Assert.Equal("sword_001", newInventory.Items[0].ItemId);
        
        // Original unchanged (immutability)
        Assert.Empty(inventory.Items);
    }
    
    [Fact]
    public void InventoryComponent_AddItem_ThrowsWhenFull()
    {
        // Arrange
        var items = new List<InventoryItem>
        {
            new() { ItemId = "item1" },
            new() { ItemId = "item2" }
        };
        var inventory = new InventoryComponent(items, maxCapacity: 2);
        var newItem = new InventoryItem { ItemId = "item3" };
        
        // Act & Assert
        Assert.Throws<InvalidOperationException>(() => inventory.AddItem(newItem));
    }
    
    [Fact]
    public void InventoryComponent_AddItem_AllowsWhenUnlimitedCapacity()
    {
        // Arrange
        var inventory = new InventoryComponent(null, maxCapacity: -1);
        
        // Act - Add many items
        var updated = inventory;
        for (int i = 0; i < 100; i++)
        {
            updated = updated.AddItem(new InventoryItem { ItemId = $"item_{i}" });
        }
        
        // Assert
        Assert.Equal(100, updated.Items.Count);
    }
    
    [Fact]
    public void InventoryComponent_RemoveItem_RemovesItemFromInventory()
    {
        // Arrange
        var items = new List<InventoryItem>
        {
            new() { ItemId = "sword_001", Name = "Iron Sword" },
            new() { ItemId = "shield_001", Name = "Wooden Shield" }
        };
        var inventory = new InventoryComponent(items);
        
        // Act
        var newInventory = inventory.RemoveItem("sword_001");
        
        // Assert
        Assert.Single(newInventory.Items);
        Assert.Equal("shield_001", newInventory.Items[0].ItemId);
        
        // Original unchanged (immutability)
        Assert.Equal(2, inventory.Items.Count);
    }
    
    [Fact]
    public void InventoryComponent_RemoveItem_ReturnsUnchangedWhenNotFound()
    {
        // Arrange
        var items = new List<InventoryItem>
        {
            new() { ItemId = "sword_001" }
        };
        var inventory = new InventoryComponent(items);
        
        // Act
        var newInventory = inventory.RemoveItem("nonexistent");
        
        // Assert
        Assert.Single(newInventory.Items);
    }
    
    [Fact]
    public void InventoryComponent_GetItem_ReturnsItemWhenExists()
    {
        // Arrange
        var items = new List<InventoryItem>
        {
            new() { ItemId = "sword_001", Name = "Iron Sword" },
            new() { ItemId = "shield_001", Name = "Wooden Shield" }
        };
        var inventory = new InventoryComponent(items);
        
        // Act
        var item = inventory.GetItem("sword_001");
        
        // Assert
        Assert.NotNull(item);
        Assert.Equal("Iron Sword", item.Name);
    }
    
    [Fact]
    public void InventoryComponent_GetItem_ReturnsNullWhenNotExists()
    {
        // Arrange
        var inventory = new InventoryComponent();
        
        // Act
        var item = inventory.GetItem("nonexistent");
        
        // Assert
        Assert.Null(item);
    }
    
    [Fact]
    public void InventoryComponent_GetEquippedItems_ReturnsOnlyEquipped()
    {
        // Arrange
        var items = new List<InventoryItem>
        {
            new() { ItemId = "sword_001", Name = "Iron Sword", IsEquipped = true },
            new() { ItemId = "shield_001", Name = "Wooden Shield", IsEquipped = false },
            new() { ItemId = "helmet_001", Name = "Iron Helmet", IsEquipped = true }
        };
        var inventory = new InventoryComponent(items);
        
        // Act
        var equipped = inventory.GetEquippedItems().ToList();
        
        // Assert
        Assert.Equal(2, equipped.Count);
        Assert.Contains(equipped, i => i.ItemId == "sword_001");
        Assert.Contains(equipped, i => i.ItemId == "helmet_001");
    }
    
    // ==================== STATS COMPONENT TESTS ====================
    
    [Fact]
    public void StatsComponent_DefaultConstruction_SetsAllStatsToTen()
    {
        // Arrange & Act
        var stats = new StatsComponent();
        
        // Assert
        Assert.Equal(10f, stats.Strength);
        Assert.Equal(10f, stats.Dexterity);
        Assert.Equal(10f, stats.Intelligence);
        Assert.Equal(10f, stats.Constitution);
        Assert.Equal(10f, stats.Wisdom);
        Assert.Equal(10f, stats.Charisma);
        Assert.Empty(stats.CustomStats);
    }
    
    [Fact]
    public void StatsComponent_FullConstruction_SetsAllStats()
    {
        // Arrange & Act
        var stats = new StatsComponent(
            strength: 15,
            dexterity: 12,
            intelligence: 8,
            constitution: 14,
            wisdom: 10,
            charisma: 9
        );
        
        // Assert
        Assert.Equal(15f, stats.Strength);
        Assert.Equal(12f, stats.Dexterity);
        Assert.Equal(8f, stats.Intelligence);
        Assert.Equal(14f, stats.Constitution);
        Assert.Equal(10f, stats.Wisdom);
        Assert.Equal(9f, stats.Charisma);
    }
    
    [Fact]
    public void StatsComponent_ConstructionWithCustomStats_SetsCustomStats()
    {
        // Arrange
        var customStats = new Dictionary<string, float>
        {
            ["luck"] = 7,
            ["spirit"] = 11
        };
        
        // Act
        var stats = new StatsComponent(customStats: customStats);
        
        // Assert
        Assert.Equal(2, stats.CustomStats.Count);
        Assert.Equal(7f, stats.CustomStats["luck"]);
        Assert.Equal(11f, stats.CustomStats["spirit"]);
    }
    
    [Fact]
    public void StatsComponent_GetStat_ReturnsCorrectBaseStats()
    {
        // Arrange
        var stats = new StatsComponent(strength: 15, dexterity: 12);
        
        // Act & Assert
        Assert.Equal(15f, stats.GetStat("strength"));
        Assert.Equal(15f, stats.GetStat("str"));
        Assert.Equal(12f, stats.GetStat("dexterity"));
        Assert.Equal(12f, stats.GetStat("dex"));
    }
    
    [Fact]
    public void StatsComponent_GetStat_IsCaseInsensitive()
    {
        // Arrange
        var stats = new StatsComponent(strength: 15);
        
        // Act & Assert
        Assert.Equal(15f, stats.GetStat("STRENGTH"));
        Assert.Equal(15f, stats.GetStat("Strength"));
        Assert.Equal(15f, stats.GetStat("STR"));
    }
    
    [Fact]
    public void StatsComponent_GetStat_ReturnsCustomStats()
    {
        // Arrange
        var customStats = new Dictionary<string, float> { ["luck"] = 7 };
        var stats = new StatsComponent(customStats: customStats);
        
        // Act
        var luck = stats.GetStat("luck");
        
        // Assert
        Assert.Equal(7f, luck);
    }
    
    [Fact]
    public void StatsComponent_GetStat_ReturnsZeroForNonexistent()
    {
        // Arrange
        var stats = new StatsComponent();
        
        // Act
        var nonexistent = stats.GetStat("nonexistent");
        
        // Assert
        Assert.Equal(0f, nonexistent);
    }
    
    [Fact]
    public void StatsComponent_WithStat_ModifiesBaseStat()
    {
        // Arrange
        var stats = new StatsComponent(strength: 10);
        
        // Act
        var modified = stats.WithStat("strength", 15);
        
        // Assert
        Assert.Equal(15f, modified.Strength);
        Assert.Equal(10f, stats.Strength); // Original unchanged
    }
    
    [Fact]
    public void StatsComponent_WithStat_WorksWithAbbreviations()
    {
        // Arrange
        var stats = new StatsComponent();
        
        // Act
        var modified = stats.WithStat("str", 15);
        
        // Assert
        Assert.Equal(15f, modified.Strength);
    }
    
    [Fact]
    public void StatsComponent_WithStat_AddsCustomStat()
    {
        // Arrange
        var stats = new StatsComponent();
        
        // Act
        var modified = stats.WithStat("luck", 7);
        
        // Assert
        Assert.Equal(7f, modified.GetStat("luck"));
    }
    
    [Fact]
    public void StatsComponent_WithStat_PreservesOtherStats()
    {
        // Arrange
        var stats = new StatsComponent(strength: 15, dexterity: 12);
        
        // Act
        var modified = stats.WithStat("strength", 20);
        
        // Assert
        Assert.Equal(20f, modified.Strength);
        Assert.Equal(12f, modified.Dexterity); // Preserved
    }
    
    // ==================== REALISTIC SCENARIOS ====================
    
    [Fact]
    public void InventoryComponent_RPGScenario_EquipmentManagement()
    {
        // Arrange - Start with empty inventory
        var inventory = new InventoryComponent(null, maxCapacity: 20);
        
        // Act - Collect items
        var sword = new InventoryItem { ItemId = "iron_sword", Name = "Iron Sword", IsEquipped = true };
        var shield = new InventoryItem { ItemId = "wooden_shield", Name = "Wooden Shield", IsEquipped = true };
        var potion = new InventoryItem { ItemId = "health_potion", Name = "Health Potion", Quantity = 5 };
        
        var withSword = inventory.AddItem(sword);
        var withShield = withSword.AddItem(shield);
        var withPotion = withShield.AddItem(potion);
        
        // Assert
        Assert.Equal(3, withPotion.Items.Count);
        var equipped = withPotion.GetEquippedItems().ToList();
        Assert.Equal(2, equipped.Count);
    }
    
    [Fact]
    public void StatsComponent_CharacterCreation_WarriorBuild()
    {
        // Arrange & Act - Create warrior with high STR and CON
        var warrior = new StatsComponent(
            strength: 18,
            dexterity: 12,
            intelligence: 8,
            constitution: 16,
            wisdom: 10,
            charisma: 10
        );
        
        // Assert
        Assert.Equal(18f, warrior.Strength);
        Assert.Equal(16f, warrior.Constitution);
        Assert.True(warrior.Strength > warrior.Intelligence);
    }
    
    [Fact]
    public void StatsComponent_LevelUp_IncreaseStats()
    {
        // Arrange - Level 1 character
        var level1 = new StatsComponent(strength: 10, constitution: 10);
        
        // Act - Level up, gain 2 STR and 1 CON
        var level2 = level1.WithStat("strength", 12).WithStat("constitution", 11);
        
        // Assert
        Assert.Equal(12f, level2.Strength);
        Assert.Equal(11f, level2.Constitution);
    }
    
    [Fact]
    public void InventoryComponent_LootingScenario_FindAndEquipItem()
    {
        // Arrange - Player finds a legendary sword
        var inventory = new InventoryComponent();
        var legendarySword = new InventoryItem
        {
            ItemId = "legendary_sword_001",
            Name = "Excalibur",
            IsEquipped = false,
            CustomData = new Dictionary<string, object> { ["damage"] = 50, ["rarity"] = "legendary" }
        };
        
        // Act - Add to inventory
        var withSword = inventory.AddItem(legendarySword);
        
        // Act - Equip it
        var equippedSword = legendarySword with { IsEquipped = true };
        var withEquipped = new InventoryComponent(new List<InventoryItem> { equippedSword });
        
        // Assert
        Assert.Single(withEquipped.GetEquippedItems());
        var item = withEquipped.GetItem("legendary_sword_001");
        Assert.NotNull(item);
        Assert.True(item.IsEquipped);
    }
    
    [Fact]
    public void StatsComponent_CustomStats_AddUniqueAttributes()
    {
        // Arrange & Act - Character with custom "luck" stat
        var customStats = new Dictionary<string, float>
        {
            ["luck"] = 15,
            ["honor"] = 20
        };
        var character = new StatsComponent(customStats: customStats);
        
        // Assert
        Assert.Equal(15f, character.GetStat("luck"));
        Assert.Equal(20f, character.GetStat("honor"));
    }
}
