namespace API.Models.StatusEffects;

/// <summary>
/// DTO para definição de status effect
/// </summary>
public class StatusEffectDefinitionDto
{
    /// <summary>
    /// ID único do status effect
    /// </summary>
    public string StatusId { get; set; } = string.Empty;
    
    /// <summary>
    /// Tipo do status effect (BURNING, POISON, STRENGTH, etc.)
    /// </summary>
    public string Type { get; set; } = "CUSTOM";
    
    /// <summary>
    /// Nome para exibição
    /// </summary>
    public string DisplayName { get; set; } = string.Empty;
    
    /// <summary>
    /// Descrição do efeito
    /// </summary>
    public string Description { get; set; } = string.Empty;
    
    /// <summary>
    /// Duração padrão em turnos (-1 = permanente)
    /// </summary>
    public int DefaultDuration { get; set; } = -1;
    
    /// <summary>
    /// Número padrão de stacks ao aplicar
    /// </summary>
    public int DefaultStacks { get; set; } = 1;
    
    /// <summary>
    /// Número máximo de stacks permitidos
    /// </summary>
    public int MaxStacks { get; set; } = 99;
    
    /// <summary>
    /// Valor base do efeito
    /// </summary>
    public float BaseValue { get; set; }
    
    /// <summary>
    /// Fórmula dinâmica para calcular valor
    /// </summary>
    public string? FormulaValue { get; set; }
    
    /// <summary>
    /// Se true, o valor escala com número de stacks
    /// </summary>
    public bool ScalesWithStacks { get; set; } = true;

    /// <summary>
    /// Recurso explicitamente alterado por comportamentos de aumento/redução.
    /// </summary>
    public string? TargetResource { get; set; }
    
    /// <summary>
    /// Chave do modificador no damage pipeline
    /// </summary>
    public string? ModifierKey { get; set; }
    
    /// <summary>
    /// Fórmula para calcular o valor do modificador
    /// </summary>
    public string? ModifierFormula { get; set; }
    
    /// <summary>
    /// Caminho do ícone para UI
    /// </summary>
    public string? IconPath { get; set; }
    
    /// <summary>
    /// Cor do status effect (hex)
    /// </summary>
    public string Color { get; set; } = "#FFFFFF";
    
    /// <summary>
    /// Tags para categorização e sinergias
    /// </summary>
    public List<string> Tags { get; set; } = new();
    
    /// <summary>
    /// Intervalo de tick (0 = não faz tick)
    /// </summary>
    public int TickInterval { get; set; }
    
    /// <summary>
    /// Se true, o status effect é permanente
    /// </summary>
    public bool IsPermanent { get; set; }
    
    /// <summary>
    /// Se true, o status effect não aparece na UI
    /// </summary>
    public bool IsHidden { get; set; }
    
    /// <summary>
    /// Se true, o status effect pode ser removido por dispel
    /// </summary>
    public bool IsDispellable { get; set; } = true;
    
    /// <summary>
    /// Dados customizados adicionais
    /// </summary>
    public Dictionary<string, object> CustomData { get; set; } = new();
}
