namespace API.Models.Combat;

public class StartCombatRequest
{
    public string HeroId { get; set; } = string.Empty;
    public List<string> Enemies { get; set; } = new();
    public int InitialEnergy { get; set; } = 3;
}
