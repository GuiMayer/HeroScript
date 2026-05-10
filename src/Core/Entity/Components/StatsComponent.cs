namespace Core.Entity.Components;

/// <summary>
/// Componente que gerencia atributos base da entidade.
/// Stats podem afetar recursos, dano, e outros cálculos via fórmulas.
/// </summary>
public class StatsComponent : ComponentBase
{
    /// <summary>
    /// Força - Afeta dano físico e HP
    /// </summary>
    public float Strength { get; init; }
    
    /// <summary>
    /// Destreza - Afeta chance de crítico e evasão
    /// </summary>
    public float Dexterity { get; init; }
    
    /// <summary>
    /// Inteligência - Afeta dano mágico e mana
    /// </summary>
    public float Intelligence { get; init; }
    
    /// <summary>
    /// Constituição - Afeta HP máximo
    /// </summary>
    public float Constitution { get; init; }
    
    /// <summary>
    /// Sabedoria - Afeta resistências e regeneração
    /// </summary>
    public float Wisdom { get; init; }
    
    /// <summary>
    /// Carisma - Afeta companions e negociação
    /// </summary>
    public float Charisma { get; init; }
    
    /// <summary>
    /// Stats customizados adicionais
    /// </summary>
    public IReadOnlyDictionary<string, float> CustomStats { get; init; } = 
        new Dictionary<string, float>();
    
    public StatsComponent(
        float strength = 10,
        float dexterity = 10,
        float intelligence = 10,
        float constitution = 10,
        float wisdom = 10,
        float charisma = 10,
        Dictionary<string, float>? customStats = null)
    {
        Strength = strength;
        Dexterity = dexterity;
        Intelligence = intelligence;
        Constitution = constitution;
        Wisdom = wisdom;
        Charisma = charisma;
        CustomStats = customStats ?? new Dictionary<string, float>();
    }
    
    /// <summary>
    /// Obtém um stat por nome (base ou custom)
    /// </summary>
    public float GetStat(string statName)
    {
        return statName.ToLowerInvariant() switch
        {
            "strength" or "str" => Strength,
            "dexterity" or "dex" => Dexterity,
            "intelligence" or "int" => Intelligence,
            "constitution" or "con" => Constitution,
            "wisdom" or "wis" => Wisdom,
            "charisma" or "cha" => Charisma,
            _ => CustomStats.TryGetValue(statName, out var value) ? value : 0f
        };
    }
    
    /// <summary>
    /// Cria uma cópia com um stat modificado
    /// </summary>
    public StatsComponent WithStat(string statName, float value)
    {
        return statName.ToLowerInvariant() switch
        {
            "strength" or "str" => new StatsComponent(value, Dexterity, Intelligence, Constitution, Wisdom, Charisma, new Dictionary<string, float>(CustomStats))
            {
                ComponentId = this.ComponentId,
                IsEnabled = this.IsEnabled
            },
            "dexterity" or "dex" => new StatsComponent(Strength, value, Intelligence, Constitution, Wisdom, Charisma, new Dictionary<string, float>(CustomStats))
            {
                ComponentId = this.ComponentId,
                IsEnabled = this.IsEnabled
            },
            "intelligence" or "int" => new StatsComponent(Strength, Dexterity, value, Constitution, Wisdom, Charisma, new Dictionary<string, float>(CustomStats))
            {
                ComponentId = this.ComponentId,
                IsEnabled = this.IsEnabled
            },
            "constitution" or "con" => new StatsComponent(Strength, Dexterity, Intelligence, value, Wisdom, Charisma, new Dictionary<string, float>(CustomStats))
            {
                ComponentId = this.ComponentId,
                IsEnabled = this.IsEnabled
            },
            "wisdom" or "wis" => new StatsComponent(Strength, Dexterity, Intelligence, Constitution, value, Charisma, new Dictionary<string, float>(CustomStats))
            {
                ComponentId = this.ComponentId,
                IsEnabled = this.IsEnabled
            },
            "charisma" or "cha" => new StatsComponent(Strength, Dexterity, Intelligence, Constitution, Wisdom, value, new Dictionary<string, float>(CustomStats))
            {
                ComponentId = this.ComponentId,
                IsEnabled = this.IsEnabled
            },
            _ => new StatsComponent(Strength, Dexterity, Intelligence, Constitution, Wisdom, Charisma, 
                new Dictionary<string, float>(CustomStats) { [statName] = value })
            {
                ComponentId = this.ComponentId,
                IsEnabled = this.IsEnabled
            }
        };
    }
}
