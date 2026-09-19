using Core.Combat.Models;
using Core.Determinism;
using Core.Effects;
using Core.Math;
using Core.Resources;
using Core.Run;
using Core.CardZones;
using Moq;
using Xunit;

namespace Core.Tests.Run;

public sealed class RunProgressionTests
{
    private readonly RunActivityRegistry _activities = RunActivityRegistry.CreateDefault();

    [Fact]
    public void ConfiguredActivities_DriveACompleteRunLoop()
    {
        var map = RunMapTransitions.Create(
        [
            Node("combat", RunActivityType.Encounter, next: ["reward"]),
            Node("reward", RunActivityType.CardSelection, "basic_reward", next: ["shop"]),
            Node("shop", RunActivityType.Shop, "basic_shop", RunActivityCompletionPolicy.Optional, ["boss"]),
            Node("boss", RunActivityType.Encounter)
        ], _activities).Value;
        var progression = new RunProgressionService(_activities);
        var run = State("combat", map, new RunProgressionPolicyDefinition
        {
            ProgressionPolicyId = "test",
            EncounterVictory = RunProgressionTransition.Continue,
            EndOfMap = RunProgressionTransition.Complete
        });

        run = CompleteEncounter(run, progression, "combat", CombatStatus.VICTORY);
        run = RunMapTransitions.Advance(run, "reward").Value.State;
        run = run with
        {
            CardSelections = [new CardSelectionState
            {
                SelectionInstanceId = Guid.Parse("10000000-0000-0000-0000-000000000001"),
                RunId = run.RunId,
                NodeId = "reward",
                SelectionId = "basic_reward",
                Completed = true
            }]
        };
        run = CompleteNode(run, progression, "reward");
        run = RunMapTransitions.Advance(run, "shop").Value.State;

        var shopCommands = progression.GetAvailableCommands(run).Value;
        Assert.Contains(shopCommands, command => command.Type == RunCommandTypes.CreateShop);
        Assert.Contains(shopCommands, command => command.Type == RunCommandTypes.ResolveNode);
        run = CompleteNode(run, progression, "shop");
        run = RunMapTransitions.Advance(run, "boss").Value.State;
        run = CompleteEncounter(run, progression, "boss", CombatStatus.VICTORY);

        Assert.Equal(RunLifecycleState.Completed, run.Lifecycle);
        Assert.Equal(new[] { "boss", "combat", "reward", "shop" }, run.Map.ResolvedNodeIds);
    }

    [Fact]
    public void EncounterOutcomesAndRetry_AreSelectedOnlyByPinnedPolicy()
    {
        var service = new RunProgressionService(_activities);
        var map = RunMapTransitions.Create([Node("combat", RunActivityType.Encounter)], _activities).Value;
        var strict = State("combat", map, new RunProgressionPolicyDefinition
        {
            ProgressionPolicyId = "strict",
            EncounterDefeat = RunProgressionTransition.Fail
        });
        var sandbox = State("combat", map, new RunProgressionPolicyDefinition
        {
            ProgressionPolicyId = "sandbox",
            EncounterDefeat = RunProgressionTransition.Continue,
            EncounterRetry = RunEncounterRetryPolicy.RestartActivity,
            RetryableEncounterOutcomes = [CombatStatus.DEFEAT]
        });

        Assert.Equal(RunLifecycleState.Failed,
            service.ApplyEncounterOutcome(strict, CombatStatus.DEFEAT).Lifecycle);
        Assert.Equal(RunLifecycleState.Active,
            service.ApplyEncounterOutcome(sandbox, CombatStatus.DEFEAT).Lifecycle);
        Assert.False(service.ShouldRestartEncounterActivity(strict, CombatStatus.DEFEAT));
        Assert.True(service.ShouldRestartEncounterActivity(sandbox, CombatStatus.DEFEAT));
        Assert.False(service.ShouldRestartEncounterActivity(sandbox, CombatStatus.VICTORY));
    }

    [Fact]
    public void EncounterRetry_LeavesNodeOpenWhileDisabledRetryFailsTheRun()
    {
        var retryable = ResolveDefeat(new RunProgressionPolicyDefinition
        {
            ProgressionPolicyId = "retry",
            EncounterDefeat = RunProgressionTransition.Fail,
            EncounterRetry = RunEncounterRetryPolicy.RestartActivity,
            RetryableEncounterOutcomes = [CombatStatus.DEFEAT]
        });
        var terminal = ResolveDefeat(new RunProgressionPolicyDefinition
        {
            ProgressionPolicyId = "terminal",
            EncounterDefeat = RunProgressionTransition.Fail,
            EncounterRetry = RunEncounterRetryPolicy.Disabled
        });

        Assert.Equal(RunLifecycleState.Active, retryable.Lifecycle);
        Assert.Empty(retryable.Map.ResolvedNodeIds);
        Assert.Null(retryable.ActiveEncounterId);
        Assert.True(Assert.Single(retryable.Encounters).Resolved);
        Assert.Equal(RunCommandTypes.StartEncounter,
            Assert.Single(new RunProgressionService(_activities).GetAvailableCommands(retryable).Value).Type);
        Assert.Equal(RunLifecycleState.Failed, terminal.Lifecycle);
        Assert.Contains("combat", terminal.Map.ResolvedNodeIds);
    }

    [Fact]
    public void ResolveCombatAvailableCommand_UsesCombatStepExpectedByTheGateway()
    {
        var combatId = Guid.Parse("50000000-0000-0000-0000-000000000001");
        var map = RunMapTransitions.Create([Node("combat", RunActivityType.Encounter)], _activities).Value;
        var run = State("combat", map, new RunProgressionPolicyDefinition()) with
        {
            Sequence = 9,
            Determinism = Enumerable.Range(0, 46).Aggregate(
                DeterministicContext.Create(42, Revision), (current, _) => current.AdvanceStep()),
            ActiveEncounterId = combatId,
            Encounters = [new RunEncounterState
            {
                NodeId = "combat",
                Combat = new CombatState
                {
                    CombatId = combatId,
                    Status = CombatStatus.VICTORY,
                    Determinism = Enumerable.Range(0, 11).Aggregate(
                        DeterministicContext.Create(7, Revision), (current, _) => current.AdvanceStep())
                }
            }]
        };

        var command = new RunProgressionService(_activities).GetAvailableCommands(run).Value
            .Single(item => item.Type == RunCommandTypes.ResolveCombat);

        Assert.Equal(9, command.ExpectedSequence);
        Assert.Equal(11UL, command.ExpectedStep);
    }

    [Fact]
    public void EndOfMapAndAbandon_UseProgressionPolicyInsteadOfResourceNames()
    {
        var service = new RunProgressionService(_activities);
        var map = RunMapTransitions.Create([Node("last", RunActivityType.Shop, "shop",
            RunActivityCompletionPolicy.Optional)], _activities).Value;
        var run = State("last", map, new RunProgressionPolicyDefinition
        {
            ProgressionPolicyId = "failure-ending",
            EndOfMap = RunProgressionTransition.Fail,
            AllowAbandon = true
        });
        var node = Assert.Single(run.Map.Nodes);

        Assert.Equal(RunLifecycleState.Failed, service.ApplyNodeExit(run, node).Lifecycle);
        var abandoned = service.Abandon(run);
        Assert.True(abandoned.IsSuccess, abandoned.IsFailure ? abandoned.Error : null);
        Assert.Equal(RunLifecycleState.Abandoned, abandoned.Value.Lifecycle);
        Assert.Equal(run.Determinism.Step + 1, abandoned.Value.Determinism.Step);
    }

    [Fact]
    public void AvailableCommands_ExposeVersionSchemaAndNodeScopedOptions()
    {
        var map = RunMapTransitions.Create(
        [Node("reward", RunActivityType.CardSelection, "same_reward")], _activities).Value;
        var run = State("reward", map, new RunProgressionPolicyDefinition()) with
        {
            Sequence = 7,
            Determinism = DeterministicContext.Create(42, Revision).AdvanceStep().AdvanceStep(),
            CardSelections = [new CardSelectionState
            {
                SelectionInstanceId = Guid.Parse("20000000-0000-0000-0000-000000000001"),
                NodeId = "an-earlier-node",
                SelectionId = "same_reward",
                Completed = true
            }]
        };

        var command = Assert.Single(new RunProgressionService(_activities)
            .GetAvailableCommands(run).Value);

        Assert.Equal(RunCommandTypes.CreateCardSelection, command.Type);
        Assert.Equal(7, command.ExpectedSequence);
        Assert.Equal(2UL, command.ExpectedStep);
        Assert.Equal("string", command.PayloadSchema.GetProperty("selectionId").GetString());
        Assert.Equal("same_reward", command.ValidPayload.GetProperty("selectionId").GetString());
    }

    [Fact]
    public void EncounterAvailableCommand_ExposesParticipantsPinnedInTheRunMap()
    {
        var participants = System.Text.Json.JsonSerializer.SerializeToElement(new object[]
        {
            new { instanceId = "player", definitionId = "player_warrior", sideId = "player",
                controllerBinding = new { kind = "Player" } },
            new { instanceId = "opponent", definitionId = "enemy_goblin", sideId = "opposition",
                controllerBinding = new { kind = "AI", policyId = "gambit" } }
        });
        var node = Node("combat", RunActivityType.Encounter) with
        {
            Activity = new RunActivityDefinition
            {
                Type = RunActivityType.Encounter,
                Parameters = new Dictionary<string, System.Text.Json.JsonElement>
                {
                    ["participants"] = participants
                }
            }
        };
        var map = RunMapTransitions.Create([node], _activities).Value;
        var run = State("combat", map, new RunProgressionPolicyDefinition());

        var command = Assert.Single(new RunProgressionService(_activities)
            .GetAvailableCommands(run).Value);

        Assert.Equal(RunCommandTypes.StartEncounter, command.Type);
        Assert.Equal("array", command.PayloadSchema.GetProperty("participants").GetString());
        var configured = command.ValidPayload.GetProperty("participants");
        Assert.Equal(2, configured.GetArrayLength());
        Assert.Equal("enemy_goblin", configured[1].GetProperty("definitionId").GetString());
    }

    [Fact]
    public void ActivityCommand_IsRejectedOutsideItsCurrentActivity()
    {
        var map = RunMapTransitions.Create([Node("combat", RunActivityType.Encounter)], _activities).Value;
        var run = State("combat", map, new RunProgressionPolicyDefinition
        {
            ProgressionPolicyId = "strict",
            AllowOutOfActivityCommands = false
        });

        var invalid = new RunProgressionService(_activities).ValidateCommand(
            run,
            RunCommandTypes.CreateShop,
            new ShopDefinitionCommand("basic_shop"));

        Assert.True(invalid.IsFailure);
        Assert.Contains("not legal for activity Encounter", invalid.Error);
    }

    [Fact]
    public void CardUpgradeActivity_BecomesResolvableOnlyAfterAnUpgrade()
    {
        var map = RunMapTransitions.Create([Node("forge", RunActivityType.CardUpgrade)], _activities).Value;
        var run = State("forge", map, new RunProgressionPolicyDefinition());
        var service = new RunProgressionService(_activities);

        Assert.DoesNotContain(service.GetAvailableCommands(run).Value,
            command => command.Type == RunCommandTypes.ResolveNode);
        run = run with { CompletedActivityNodeIds = ["forge"] };
        Assert.Equal(RunCommandTypes.ResolveNode,
            Assert.Single(service.GetAvailableCommands(run).Value).Type);
    }

    [Fact]
    public void CardUpgradeOptions_ExposeOnlyApplicablePairsWithRemainingApplications()
    {
        var system = CardZoneSystemCompiler.Compile(new CardZoneSystemDefinition
        {
            CardZoneSystemId = "upgrade-options",
            Zones = [new CardZoneDefinition
            {
                ZoneId = "deck",
                OwnerScope = CardZoneOwnerScope.RunOwner,
                Ordering = CardZoneOrdering.Ordered
            }]
        }).Value;
        var topology = CardZoneBootstrapper.Create(system, new CardZoneBootstrapPlan
        {
            RunOwnerId = "$run",
            Batches = [new CardZoneInitialBatch
            {
                ZoneId = "deck",
                OwnerId = "$run",
                Cards =
                [
                    new CardZoneCardCreation
                    {
                        DefinitionId = "strike",
                        Upgrades = [new CardUpgradeState { UpgradeId = "sharp" }]
                    },
                    new CardZoneCardCreation { DefinitionId = "guard" }
                ]
            }]
        }, DeterministicContext.Create(42, Revision)).Value.State;
        var run = State("forge", new RunMapState(), new RunProgressionPolicyDefinition()) with
        {
            Deck = new DeckState { Topology = topology }
        };

        var options = CardUpgradeCommandOptions.Project(run,
        [
            new CardUpgradeDefinition
            {
                UpgradeId = "sharp",
                CardDefinitionIds = ["strike"],
                MaxApplications = 1
            },
            new CardUpgradeDefinition
            {
                UpgradeId = "reinforced",
                CardDefinitionIds = ["guard"]
            }
        ]);

        var option = Assert.Single(options);
        Assert.Equal("guard", option.CardDefinitionId);
        Assert.Equal("reinforced", option.UpgradeId);
    }

    [Fact]
    public void RelicRewardActivity_AdvertisesPinnedRelicAndBecomesResolvableAfterAcquisition()
    {
        var map = RunMapTransitions.Create(
            [Node("relic", RunActivityType.RelicReward, "ember_core")], _activities).Value;
        var run = State("relic", map, new RunProgressionPolicyDefinition());
        var service = new RunProgressionService(_activities);

        var acquire = Assert.Single(service.GetAvailableCommands(run).Value);
        Assert.Equal(RunCommandTypes.AcquireRelic, acquire.Type);
        Assert.Equal("ember_core", acquire.ValidPayload.GetProperty("relicId").GetString());
        Assert.True(service.ValidateCommand(run, RunCommandTypes.AcquireRelic,
            new RelicCommand("another_relic")).IsFailure);

        run = run with { CompletedActivityNodeIds = ["relic"] };
        Assert.Equal(RunCommandTypes.ResolveNode,
            Assert.Single(service.GetAvailableCommands(run).Value).Type);
    }

    [Fact]
    public void ActivityBoundary_UsesUniversalImmutableEffectExecutor()
    {
        var effect = new EffectDefinition
        {
            EffectId = "entry-gold",
            Type = EffectType.MODIFY_RESOURCE,
            Target = EffectTarget.SELF,
            TargetResource = "gold",
            Operation = ResourceEffectOperation.ADD,
            FlatValue = 3
        };
        var node = Node("shop", RunActivityType.Shop, "shop") with { EntryEffects = [effect] };
        var run = State("shop", RunMapTransitions.Create([node], _activities).Value,
            new RunProgressionPolicyDefinition()) with
        {
            ResolvedMode = null,
            ResourceState = new ResourceSet
            {
                OwnerId = "player",
                Resources = new Dictionary<string, ResourcePool>(StringComparer.OrdinalIgnoreCase)
                {
                    ["gold"] = ResourcePool.Materialize(new ResourceDefinition
                    {
                        ResourceId = "gold",
                        DisplayName = "Gold",
                        DefaultCurrent = 5,
                        DefaultMax = 100
                    })
                }
            }
        };
        var executor = new RunActivityEffectExecutor(new EffectTriggerExecutor(
            Mock.Of<IRuntimeFormulaEvaluator>(), new ImmutableEffectProcessor(),
            allowUnconfiguredCalculations: true));

        var first = executor.Execute(run, run.Map.Nodes[0], RunActivityBoundary.Entry);
        var repeated = executor.Execute(run, run.Map.Nodes[0], RunActivityBoundary.Entry);

        Assert.True(first.IsSuccess, first.IsFailure ? first.Error : null);
        Assert.Equal(8, first.Value.State.ResourceState.Resources["gold"].Current);
        Assert.Equal(5, run.ResourceState.Resources["gold"].Current);
        Assert.Single(first.Value.Steps);
        Assert.Equal(EffectProvenanceKind.Rule, first.Value.Steps[0].Provenance.Kind);
        Assert.Equal(first.Value.Fingerprint, repeated.Value.Fingerprint);
    }

    [Fact]
    public void RegistryRejectsUnknownActivityAndMissingRequiredDefinition()
    {
        Assert.True(_activities.Validate(new RunActivityDefinition
        {
            Type = RunActivityType.Shop
        }).IsFailure);
        Assert.True(_activities.Validate(new RunActivityDefinition
        {
            Type = (RunActivityType)999
        }).IsFailure);
    }

    private static RunState CompleteEncounter(
        RunState run,
        RunProgressionService progression,
        string nodeId,
        CombatStatus outcome)
    {
        var combatId = Guid.NewGuid();
        run = run with
        {
            ActiveEncounterId = combatId,
            Encounters = run.Encounters.Add(new RunEncounterState
            {
                NodeId = nodeId,
                Resolved = true,
                Outcome = outcome.ToString(),
                Combat = new CombatState { CombatId = combatId, Status = outcome }
            })
        };
        var node = run.Map.Nodes.Single(item => item.NodeId == nodeId);
        Assert.True(progression.CanResolve(run, node).IsSuccess);
        var resolved = RunMapTransitions.Resolve(run, nodeId).Value;
        var next = progression.ApplyNodeExit(resolved.State, resolved.Value);
        return progression.ApplyEncounterOutcome(next, outcome);
    }

    private RunState ResolveDefeat(RunProgressionPolicyDefinition policy)
    {
        var combatId = Guid.NewGuid();
        var map = RunMapTransitions.Create([Node("combat", RunActivityType.Encounter)], _activities).Value;
        var zones = new CardZoneSystemDefinition
        {
            CardZoneSystemId = "progression-test",
            Zones = [new()
            {
                ZoneId = "cards", OwnerScope = CardZoneOwnerScope.RunOwner,
                Ordering = CardZoneOrdering.Ordered
            }]
        };
        var cards = CardZoneBootstrapper.Create(CardZoneSystemCompiler.Compile(zones).Value,
            new CardZoneBootstrapPlan { RunOwnerId = "$run" },
            DeterministicContext.Create(1, "test")).Value;
        var run = State("combat", map, policy) with
        {
            Sequence = 4,
            ActiveEncounterId = combatId,
            Deck = new DeckState { Topology = cards.State },
            Determinism = cards.Context,
            ResolvedMode = new ResolvedGameMode
            {
                ProgressionPolicy = policy,
                CardZoneSystem = zones,
                CombatRules = new() { Flow = new() }
            },
            Encounters = [new RunEncounterState
            {
                NodeId = "combat",
                Combat = new CombatState
                {
                    CombatId = combatId,
                    Status = CombatStatus.DEFEAT,
                    Actors = new Dictionary<string, CombatActorState>
                    {
                        ["player"] = new()
                        {
                            InstanceId = "player",
                            SideId = "player", ControllerBinding = new ControllerBinding { Kind = ControllerKind.Player },
                            ResourceState = new ResourceSet { OwnerId = "player" }
                        }
                    }
                }
            }]
        };
        var manager = new RunManager(
            Mock.Of<Core.Config.IConfigManager>(),
            Mock.Of<Core.Config.IResourceLoader>(),
            activities: _activities,
            cardZoneFlows: new CardZoneFlowExecutor());
        Assert.True(manager.HydrateForReplay(run).IsSuccess);
        var resolved = manager.ResolveEncounter(run.RunId, run.Sequence, combatId);
        Assert.True(resolved.IsSuccess, resolved.IsFailure ? resolved.Error : null);
        return resolved.Value;
    }

    private static RunState CompleteNode(RunState run, RunProgressionService progression, string nodeId)
    {
        var node = run.Map.Nodes.Single(item => item.NodeId == nodeId);
        Assert.True(progression.CanResolve(run, node).IsSuccess);
        var resolved = RunMapTransitions.Resolve(run, nodeId).Value;
        return progression.ApplyNodeExit(resolved.State, resolved.Value);
    }

    private static RunMapNodeDefinition Node(
        string nodeId,
        RunActivityType type,
        string? definitionId = null,
        RunActivityCompletionPolicy completion = RunActivityCompletionPolicy.Required,
        IReadOnlyList<string>? next = null) => new()
    {
        NodeId = nodeId,
        Activity = new RunActivityDefinition { Type = type, DefinitionId = definitionId },
        CompletionPolicy = completion,
        NextNodeIds = next ?? []
    };

    private static RunState State(
        string nodeId,
        RunMapState map,
        RunProgressionPolicyDefinition policy) => new()
    {
        RunId = Guid.Parse("30000000-0000-0000-0000-000000000001"),
        PlayerEntityId = "player",
        CurrentNodeId = nodeId,
        Map = map,
        ResolvedMode = new ResolvedGameMode { ProgressionPolicy = policy },
        Determinism = DeterministicContext.Create(42, Revision)
    };

    private const string Revision = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
}
