namespace API.Models.Actions;

/// <summary>
/// Response de validação de ação
/// </summary>
public class ActionValidationResponse
{
    public bool IsValid { get; set; }
    public List<string> Errors { get; set; } = new();
}
