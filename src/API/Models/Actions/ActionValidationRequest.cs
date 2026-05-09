namespace API.Models.Actions;

/// <summary>
/// Request para validar uma definição de ação
/// </summary>
public class ActionValidationRequest
{
    public ActionDefinitionDto Definition { get; set; } = new();
}
