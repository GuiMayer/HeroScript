using Core.Combat;
using Core.Combat.Models;
using Core.Resources;
using Xunit;

namespace Core.Tests.Combat;

/// <summary>
/// Testes de integração demonstrando o uso completo do sistema de custos alternativos.
/// Estes testes mostram como o sistema funcionará quando integrado com ActionManager.
/// </summary>
public class AlternativeCostsIntegrationTests
{
    private Dictionary<string, ResourcePool> CreateHeroResources()
    {
        var manaDefinition = new ResourceDefinition
        {
            ResourceId = "mana",
            DisplayName = "Mana",
            Category = ResourceCategory.TACTICAL,
            DefaultMin = 0,
            DefaultMax = 10,
            DefaultCurrent = 5,
            CanBeNegative = false
        };

        var healthDefinition = new ResourceDefinition
        {
            ResourceId = "health",
            DisplayName = "Health",
            Category = ResourceCategory.VITAL,
            DefaultMin = 0,
            DefaultMax = 100,
            DefaultCurrent = 80,
            CanBeNegative = false
        };

        return new Dictionary<string, ResourcePool>
        {
            ["mana"] = new ResourcePool
            {
                Definition = manaDefinition,
                Current = 5,
                Maximum = 10,
                Minimum = 0
            },
            ["health"] = new ResourcePool
            {
                Definition = healthDefinition,
                Current = 80,
                Maximum = 100,
                Minimum = 0
            }
        };
    }

    [Fact]
    public void Scenario_PowerfulSpell_WithAlternativeCosts()
    {
        // Arrange: Simular uma ação "Powerful Spell" com custos alternativos
        var heroResources = CreateHeroResources();
        
        var powerfulSpellCosts = new ActionCosts
        {
            Costs = new List<ResourceCost>(),
            AlternativeCosts = new List<AlternativeCostOption>
            {
                new AlternativeCostOption
                {
                    OptionId = "mana_cost",
                    Description = "Pay 8 mana (insufficient)",
                    Costs = new List<ResourceCost>
                    {
                        new ResourceCost { ResourceId = "mana", Amount = 8, AllowOverdraft = false }
                    }
                },
                new AlternativeCostOption
                {
                    OptionId = "health_cost",
                    Description = "Pay 20 health (blood magic)",
                    Costs = new List<ResourceCost>
                    {
                        new ResourceCost { ResourceId = "health", Amount = 20, AllowOverdraft = false }
                    }
                },
                new AlternativeCostOption
                {
                    OptionId = "hybrid_cost",
                    Description = "Pay 3 mana + 10 health",
                    Costs = new List<ResourceCost>
                    {
                        new ResourceCost { ResourceId = "mana", Amount = 3, AllowOverdraft = false },
                        new ResourceCost { ResourceId = "health", Amount = 10, AllowOverdraft = false }
                    }
                }
            }
        };

        // Act: Verificar quais opções são acessíveis
        var affordableOptions = powerfulSpellCosts.GetAffordableOptions(heroResources);

        // Assert: Herói tem 5 mana e 80 health
        Assert.Equal(2, affordableOptions.Count); // health_cost e hybrid_cost são acessíveis
        Assert.Contains(affordableOptions, o => o.OptionId == "health_cost");
        Assert.Contains(affordableOptions, o => o.OptionId == "hybrid_cost");
        Assert.DoesNotContain(affordableOptions, o => o.OptionId == "mana_cost"); // Precisa de 8, tem 5
    }

    [Fact]
    public void Scenario_PlayerChoosesHealthCost_ResourcesAreSpent()
    {
        // Arrange: Simular escolha do jogador
        var heroResources = CreateHeroResources();
        
        var powerfulSpellCosts = new ActionCosts
        {
            Costs = new List<ResourceCost>(),
            AlternativeCosts = new List<AlternativeCostOption>
            {
                new AlternativeCostOption
                {
                    OptionId = "health_cost",
                    Description = "Pay 20 health",
                    Costs = new List<ResourceCost>
                    {
                        new ResourceCost { ResourceId = "health", Amount = 20, AllowOverdraft = false }
                    }
                }
            }
        };

        // Act: Jogador escolhe pagar com health
        var selectedOption = powerfulSpellCosts.GetOption("health_cost");
        Assert.NotNull(selectedOption);

        // Simular aplicação dos custos
        var healthPool = heroResources["health"];
        var newHealthPool = healthPool.Spend(20);

        // Assert: Health foi reduzido
        Assert.Equal(80, healthPool.Current); // Original
        Assert.Equal(60, newHealthPool.Current); // Após gastar 20
    }

    [Fact]
    public void Scenario_NoAffordableOptions_ErrorMessageIsDescriptive()
    {
        // Arrange: Herói com poucos recursos
        var heroResources = CreateHeroResources();
        heroResources["mana"] = heroResources["mana"] with { Current = 2 };
        heroResources["health"] = heroResources["health"] with { Current = 15 };
        
        var expensiveSpellCosts = new ActionCosts
        {
            Costs = new List<ResourceCost>(),
            AlternativeCosts = new List<AlternativeCostOption>
            {
                new AlternativeCostOption
                {
                    OptionId = "mana_cost",
                    Description = "Pay 8 mana",
                    Costs = new List<ResourceCost>
                    {
                        new ResourceCost { ResourceId = "mana", Amount = 8, AllowOverdraft = false }
                    }
                },
                new AlternativeCostOption
                {
                    OptionId = "health_cost",
                    Description = "Pay 20 health",
                    Costs = new List<ResourceCost>
                    {
                        new ResourceCost { ResourceId = "health", Amount = 20, AllowOverdraft = false }
                    }
                }
            }
        };

        // Act
        var canAfford = expensiveSpellCosts.CanAfford(heroResources);
        var error = expensiveSpellCosts.GetAffordabilityError(heroResources);

        // Assert
        Assert.False(canAfford);
        Assert.NotNull(error);
        Assert.Contains("Cannot afford any alternative", error);
        Assert.Contains("Pay 8 mana OR Pay 20 health", error);
    }

    [Fact]
    public void Scenario_HybridCost_RequiresBothResources()
    {
        // Arrange: Herói com mana suficiente mas health insuficiente
        var heroResources = CreateHeroResources();
        heroResources["health"] = heroResources["health"] with { Current = 5 };
        
        var hybridCostOption = new AlternativeCostOption
        {
            OptionId = "hybrid",
            Description = "Pay 3 mana + 10 health",
            Costs = new List<ResourceCost>
            {
                new ResourceCost { ResourceId = "mana", Amount = 3, AllowOverdraft = false },
                new ResourceCost { ResourceId = "health", Amount = 10, AllowOverdraft = false }
            }
        };

        // Act: Verificar se pode pagar
        var canAfford = hybridCostOption.CanAfford(heroResources);

        // Assert: Tem 5 mana (suficiente) mas apenas 5 health (insuficiente, precisa de 10)
        Assert.False(canAfford);
    }

    [Fact]
    public void Scenario_BackwardCompatibility_NormalCostsStillWork()
    {
        // Arrange: Ação tradicional sem custos alternativos
        var heroResources = CreateHeroResources();
        
        var traditionalPowerCosts = new ActionCosts
        {
            Costs = new List<ResourceCost>
            {
                new ResourceCost { ResourceId = "mana", Amount = 3, AllowOverdraft = false }
            },
            AlternativeCosts = new List<AlternativeCostOption>() // Vazio
        };

        // Act
        var canAfford = traditionalPowerCosts.CanAfford(heroResources);

        // Assert: Sistema tradicional continua funcionando
        Assert.True(canAfford);
        Assert.Empty(traditionalPowerCosts.AlternativeCosts);
    }

    [Fact]
    public void Scenario_UIFlow_GetOptionsForDisplay()
    {
        // Arrange: Simular fluxo de UI onde jogador vê opções disponíveis
        var heroResources = CreateHeroResources();
        
        var spellCosts = new ActionCosts
        {
            Costs = new List<ResourceCost>(),
            AlternativeCosts = new List<AlternativeCostOption>
            {
                new AlternativeCostOption
                {
                    OptionId = "option1",
                    Description = "Pay 8 mana",
                    Costs = new List<ResourceCost>
                    {
                        new ResourceCost { ResourceId = "mana", Amount = 8, AllowOverdraft = false }
                    }
                },
                new AlternativeCostOption
                {
                    OptionId = "option2",
                    Description = "Pay 15 health",
                    Costs = new List<ResourceCost>
                    {
                        new ResourceCost { ResourceId = "health", Amount = 15, AllowOverdraft = false }
                    }
                },
                new AlternativeCostOption
                {
                    OptionId = "option3",
                    Description = "Pay 3 mana + 10 health",
                    Costs = new List<ResourceCost>
                    {
                        new ResourceCost { ResourceId = "mana", Amount = 3, AllowOverdraft = false },
                        new ResourceCost { ResourceId = "health", Amount = 10, AllowOverdraft = false }
                    }
                }
            }
        };

        // Act: UI obtém opções acessíveis para mostrar ao jogador
        var affordableOptions = spellCosts.GetAffordableOptions(heroResources);

        // Assert: UI pode mostrar apenas opções que o jogador pode pagar
        Assert.Equal(2, affordableOptions.Count);
        
        // UI pode iterar e mostrar descrições
        foreach (var option in affordableOptions)
        {
            Assert.NotNull(option.Description);
            Assert.NotEmpty(option.Costs);
        }
    }
}
