using Core.Combat.Models;
using Core.Combat.TurnPhase;
using Xunit;

namespace Core.Tests.Combat.TurnPhase;

public sealed class PhaseSequenceValidatorTests
{
    [Fact]
    public void Validate_AcceptsReachableGraphWithMultipleMiddlePhases()
    {
        var sequence = CreateValid();
        var phases = sequence.Phases.ToList();
        phases.Insert(2, new PhaseDefinition
        {
            PhaseId = "combat_window", Role = PhaseRole.Middle, Order = 25,
            AllowedActions = [ActionType.BASIC_ATTACK, ActionType.END_TURN],
            Edges = [Edge("combat-end", "cleanup", PhaseEdgeTrigger.ActivationExit)]
        });
        phases[1] = phases[1] with
        {
            Edges = phases[1].Edges.Append(new PhaseEdgeDefinition
            {
                EdgeId = "planning-combat", TargetPhaseId = "combat_window",
                Trigger = PhaseEdgeTrigger.Command, ActionTypes = [ActionType.PASS]
            }).ToArray()
        };

        var result = PhaseSequenceValidator.Validate(sequence with { Phases = phases });

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error : null);
    }

    [Fact]
    public void Validate_RejectsSequenceWithoutEverySemanticRole()
    {
        var original = CreateValid();
        var sequence = original with
        {
            Phases = original.Phases.Select(phase => phase.Role == PhaseRole.End
                ? phase with { Role = PhaseRole.Middle }
                : phase).ToArray()
        };

        var result = PhaseSequenceValidator.Validate(sequence);

        Assert.True(result.IsFailure);
        Assert.Contains("END", result.Error);
    }

    [Fact]
    public void Validate_RejectsUnknownTransitionTarget()
    {
        var sequence = CreateValid();
        sequence = sequence with
        {
            Phases = sequence.Phases.Select(phase => phase.Role == PhaseRole.Middle
                ? phase with { Edges = [Edge("missing-edge", "missing", PhaseEdgeTrigger.ActivationExit)] }
                : phase).ToArray()
        };

        var result = PhaseSequenceValidator.Validate(sequence);

        Assert.True(result.IsFailure);
        Assert.Contains("invalid next phase", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Validate_RejectsUnreachablePhase()
    {
        var original = CreateValid();
        var sequence = original with
        {
            Phases = original.Phases.Append(new PhaseDefinition
            {
                PhaseId = "orphan", Role = PhaseRole.Middle, Order = 40,
                Edges = [Edge("orphan-end", "cleanup", PhaseEdgeTrigger.ActivationExit)]
            }).ToArray()
        };

        var result = PhaseSequenceValidator.Validate(sequence);

        Assert.True(result.IsFailure);
        Assert.Contains("unreachable", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Validate_RejectsAutomaticCycle()
    {
        var sequence = CreateValid();
        sequence = sequence with
        {
            Phases = sequence.Phases.Select(phase => phase.PhaseId switch
            {
                "planning" => phase with
                {
                    Edges = [Edge("planning-start", "upkeep", PhaseEdgeTrigger.Automatic)]
                },
                _ => phase
            }).ToArray()
        };

        var result = PhaseSequenceValidator.Validate(sequence);

        Assert.True(result.IsFailure);
        Assert.Contains("automatic cycle", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Validate_RejectsAmbiguousEdgesAtSamePriority()
    {
        var sequence = CreateValid();
        sequence = sequence with
        {
            Phases = sequence.Phases.Select(phase => phase.PhaseId == "planning"
                ? phase with
                {
                    Edges =
                    [
                        Edge("exit-a", "cleanup", PhaseEdgeTrigger.ActivationExit),
                        Edge("exit-b", "cleanup", PhaseEdgeTrigger.ActivationExit)
                    ]
                }
                : phase).ToArray()
        };

        var result = PhaseSequenceValidator.Validate(sequence);

        Assert.True(result.IsFailure);
        Assert.Contains("ambiguous", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Validate_RejectsTerminalStartOrMiddlePhase()
    {
        var sequence = CreateValid();
        sequence = sequence with
        {
            Phases = sequence.Phases.Select(phase => phase.PhaseId == "planning"
                ? phase with { Edges = [] }
                : phase).ToArray()
        };

        var result = PhaseSequenceValidator.Validate(sequence);

        Assert.True(result.IsFailure);
        Assert.Contains("dead end", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    private static PhaseSequenceDefinition CreateValid() => new()
    {
        SequenceId = "custom",
        EntryPhaseId = "upkeep",
        Name = "Custom",
        Phases =
        [
            new PhaseDefinition
            {
                PhaseId = "upkeep", Role = PhaseRole.Start, Order = 10,
                Edges = [Edge("upkeep-planning", "planning", PhaseEdgeTrigger.Automatic)]
            },
            new PhaseDefinition
            {
                PhaseId = "planning", Role = PhaseRole.Middle, Order = 20,
                AllowedActions = [ActionType.POWER, ActionType.PASS, ActionType.END_TURN],
                Edges = [Edge("planning-cleanup", "cleanup", PhaseEdgeTrigger.ActivationExit)]
            },
            new PhaseDefinition
            {
                PhaseId = "cleanup", Role = PhaseRole.End, Order = 30
            }
        ]
    };

    private static PhaseEdgeDefinition Edge(string id, string target, PhaseEdgeTrigger trigger) => new()
    {
        EdgeId = id,
        TargetPhaseId = target,
        Trigger = trigger
    };
}
