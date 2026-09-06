using Core.Combat.Models;
using Core.Combat.TurnPhase;
using Xunit;

namespace Core.Tests.Combat.TurnPhase;

public sealed class PhaseSequenceValidatorTests
{
    [Fact]
    public void Validate_AcceptsArbitraryIdsWithRequiredSemanticRoles()
    {
        var sequence = CreateValid();

        var result = PhaseSequenceValidator.Validate(sequence);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error : null);
        Assert.Equal(PhaseRole.Middle, sequence.Find("planning_window")?.Role);
        Assert.Contains(ActionType.POWER, sequence.Find("planning_window")!.AllowedActions);
    }

    [Fact]
    public void Validate_RejectsSequenceWithoutEverySemanticRole()
    {
        var sequence = CreateValid();
        sequence = sequence with
        {
            Phases = sequence.Phases.Select(phase => phase.Role == PhaseRole.End
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
                ? phase with { ValidNextPhaseIds = ["missing"] }
                : phase).ToArray()
        };

        var result = PhaseSequenceValidator.Validate(sequence);

        Assert.True(result.IsFailure);
        Assert.Contains("invalid next phase", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Validate_RejectsDuplicateOrder()
    {
        var sequence = CreateValid();
        sequence = sequence with
        {
            Phases = sequence.Phases.Select(phase => phase.Role == PhaseRole.Middle
                ? phase with { Order = 10 }
                : phase).ToArray()
        };

        var result = PhaseSequenceValidator.Validate(sequence);

        Assert.True(result.IsFailure);
        Assert.Contains("unique and ascending", result.Error);
    }

    [Fact]
    public void CanonicalActivation_RejectsMultiPhaseGraphInsteadOfIgnoringIt()
    {
        var sequence = CreateValid();
        var phases = sequence.Phases.ToList();
        phases.Insert(2, new PhaseDefinition
        {
            PhaseId = "second_planning_window",
            Role = PhaseRole.Middle,
            Order = 25,
            AllowedActions = [ActionType.PASS],
            ValidNextPhaseIds = ["cleanup_custom"]
        });
        sequence = sequence with { Phases = phases };

        Assert.True(PhaseSequenceValidator.Validate(sequence).IsSuccess);
        var canonical = PhaseSequenceValidator.ValidateCanonicalActivationSequence(sequence);
        Assert.True(canonical.IsFailure);
        Assert.Contains("exactly one MIDDLE", canonical.Error, StringComparison.Ordinal);
    }

    private static PhaseSequenceDefinition CreateValid() => new()
    {
        SequenceId = "custom",
        Name = "Custom",
        Phases =
        [
            new PhaseDefinition
            {
                PhaseId = "upkeep_custom", Role = PhaseRole.Start, Order = 10,
                ValidNextPhaseIds = ["planning_window"], AutoTransition = true
            },
            new PhaseDefinition
            {
                PhaseId = "planning_window", Role = PhaseRole.Middle, Order = 20,
                AllowedActions = [ActionType.POWER, ActionType.END_TURN],
                ValidNextPhaseIds = ["cleanup_custom"]
            },
            new PhaseDefinition
            {
                PhaseId = "cleanup_custom", Role = PhaseRole.End, Order = 30,
                ValidNextPhaseIds = ["upkeep_custom"], AutoTransition = true
            }
        ]
    };
}
