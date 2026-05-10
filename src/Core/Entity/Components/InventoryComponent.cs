namespace Core.Entity.Components;

/// <summary>
/// Item no inventário da entidade
/// </summary>
public record InventoryItem
{
    public string ItemId { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public int Quantity { get; init; } = 1;
    public bool IsEquipped { get; init; }
    public Dictionary<string, object> CustomData { get; init; } = new();
}

/// <summary>
/// Componente que gerencia inventário da entidade.
/// Placeholder para Fase 4 (Content System).
/// </summary>
public class InventoryComponent : ComponentBase
{
    /// <summary>
    /// Itens no inventário
    /// </summary>
    public IReadOnlyList<InventoryItem> Items { get; init; } = new List<InventoryItem>();
    
    /// <summary>
    /// Capacidade máxima do inventário (-1 = ilimitado)
    /// </summary>
    public int MaxCapacity { get; init; } = -1;
    
    public InventoryComponent(IReadOnlyList<InventoryItem>? items = null, int maxCapacity = -1)
    {
        Items = items ?? new List<InventoryItem>();
        MaxCapacity = maxCapacity;
    }
    
    /// <summary>
    /// Adiciona um item ao inventário
    /// </summary>
    public InventoryComponent AddItem(InventoryItem item)
    {
        if (MaxCapacity > 0 && Items.Count >= MaxCapacity)
            throw new InvalidOperationException("Inventory is full");
        
        var newItems = new List<InventoryItem>(Items) { item };
        return new InventoryComponent(newItems, MaxCapacity)
        {
            ComponentId = this.ComponentId,
            IsEnabled = this.IsEnabled
        };
    }
    
    /// <summary>
    /// Remove um item do inventário
    /// </summary>
    public InventoryComponent RemoveItem(string itemId)
    {
        var newItems = Items.Where(i => i.ItemId != itemId).ToList();
        return new InventoryComponent(newItems, MaxCapacity)
        {
            ComponentId = this.ComponentId,
            IsEnabled = this.IsEnabled
        };
    }
    
    /// <summary>
    /// Obtém um item específico
    /// </summary>
    public InventoryItem? GetItem(string itemId)
    {
        return Items.FirstOrDefault(i => i.ItemId == itemId);
    }
    
    /// <summary>
    /// Obtém todos os itens equipados
    /// </summary>
    public IEnumerable<InventoryItem> GetEquippedItems()
    {
        return Items.Where(i => i.IsEquipped);
    }
}
