using API.Models.Actions;
using API.Models.Effects;
using Core.Combat;
using Core.Effects;
using Core.Run;
using Microsoft.AspNetCore.Mvc;

namespace API.Controllers;

/// <summary>
/// Controller para aplicacao central de efeitos data-driven.
/// </summary>
[ApiController]
[Route("api/v1/simulations/effects")]
public class EffectController : BaseApiController
{
    private readonly IEffectResolver _effectResolver;
    private readonly ICombatSystem _combatSystem;
    private readonly IRunManager _runManager;

    public EffectController(
        IEffectResolver effectResolver,
        ICombatSystem combatSystem,
        IRunManager runManager,
        ILogger<EffectController> logger)
        : base(logger)
    {
        _effectResolver = effectResolver ?? throw new ArgumentNullException(nameof(effectResolver));
        _combatSystem = combatSystem ?? throw new ArgumentNullException(nameof(combatSystem));
        _runManager = runManager ?? throw new ArgumentNullException(nameof(runManager));
    }

    [HttpGet("types")]
    public IActionResult GetEffectTypes()
    {
        return Ok(Enum.GetNames<EffectType>());
    }

    [HttpGet("targets")]
    public IActionResult GetEffectTargets()
    {
        return Ok(Enum.GetNames<EffectTarget>());
    }

    [HttpGet("scopes")]
    public IActionResult GetEffectScopes()
    {
        return Ok(Enum.GetNames<EffectScope>());
    }

    [HttpPost("apply")]
    [API.Attributes.AdminEndpoint]
    public IActionResult ApplyEffect([FromBody] ApplyEffectRequest request)
    {
        try
        {
            if (request?.Effect == null)
                return BadRequest(new { error = "Effect is required" });

            if (!Enum.TryParse<EffectScope>(request.Scope, true, out var scope))
                return BadRequest(new { error = $"Invalid effect scope: {request.Scope}" });

            var effect = new EffectInstance
            {
                Definition = MapEffectFromDto(request.Effect),
                SourceEntityId = request.SourceEntityId,
                TargetEntityId = request.TargetEntityId,
                SourceActionId = request.SourceActionId,
                SourceCardId = request.SourceCardId
            };

            var contextResult = CreateContext(scope, request, effect);
            if (contextResult is not ContextBuildResult { Context: not null } context)
                return contextResult!.ActionResult!;

            var result = _effectResolver.ApplyEffect(effect, context.Context);
            if (result.IsFailure)
                return BadRequest(new { error = result.Error });

            var applicationResult = result.Value;
            if (!applicationResult.Success)
                return BadRequest(MapToResponse(applicationResult));

            return Ok(MapToResponse(applicationResult));
        }
        catch (Exception ex)
        {
            return HandleException(ex, "apply effect");
        }
    }

    private ContextBuildResult CreateContext(EffectScope scope, ApplyEffectRequest request, EffectInstance effect)
    {
        if (scope == EffectScope.COMBAT)
        {
            if (request.CombatId == null)
                return ContextBuildResult.Failure(BadRequest(new { error = "CombatId is required for combat effects" }));

            var stateResult = _combatSystem.GetCombatState(request.CombatId.Value);
            if (stateResult.IsFailure)
                return ContextBuildResult.Failure(NotFound(new { error = stateResult.Error }));

            return ContextBuildResult.Success(CombatEffectContext.FromEffect(effect, stateResult.Value));
        }

        if (scope == EffectScope.RUN)
        {
            if (!Guid.TryParse(request.RunId, out var runId))
                return ContextBuildResult.Failure(BadRequest(new { error = "RunId is required for run effects" }));

            var runResult = _runManager.GetRun(runId);
            if (runResult.IsFailure)
                return ContextBuildResult.Failure(NotFound(new { error = runResult.Error }));

            return ContextBuildResult.Success(new RunEffectContext
            {
                RunId = request.RunId ?? string.Empty,
                RunState = runResult.Value,
                SourceEntityId = request.SourceEntityId,
                TargetEntityId = request.TargetEntityId,
                SourceActionId = request.SourceActionId,
                SourceCardId = request.SourceCardId
            });
        }

        return ContextBuildResult.Failure(BadRequest(new { error = $"Scope {scope} is not supported by this endpoint yet" }));
    }

    private static ApplyEffectResponse MapToResponse(EffectApplicationResult result)
    {
        return new ApplyEffectResponse
        {
            Success = result.Success,
            ErrorMessage = result.ErrorMessage,
            Scope = result.Scope.ToString(),
            HasUpdatedCombatState = result.UpdatedCombatState != null,
            EffectResult = new EffectResultDto
            {
                Success = result.EffectResult.Success,
                ErrorMessage = result.EffectResult.ErrorMessage,
                ValueApplied = result.EffectResult.ValueApplied,
                ResourceAffected = result.EffectResult.ResourceAffected,
                AffectedEntityIds = result.EffectResult.AffectedEntityIds.ToList(),
                StatusApplied = result.EffectResult.StatusApplied.ToList(),
                StatusRemoved = result.EffectResult.StatusRemoved.ToList(),
                Metadata = result.EffectResult.Metadata.ToDictionary(item => item.Key, item => item.Value)
            }
        };
    }

    private static EffectDefinition MapEffectFromDto(EffectDefinitionDto dto)
    {
        return new EffectDefinition
        {
            EffectId = string.IsNullOrWhiteSpace(dto.EffectId) ? Guid.NewGuid().ToString() : dto.EffectId,
            Type = Enum.TryParse<EffectType>(dto.Type, true, out var type) ? type : EffectType.DAMAGE,
            Target = Enum.TryParse<EffectTarget>(dto.Target, true, out var target) ? target : EffectTarget.TARGET,
            Timing = Enum.TryParse<EffectTiming>(dto.Timing, true, out var timing) ? timing : EffectTiming.IMMEDIATE,
            FlatValue = dto.FlatValue,
            FormulaValue = dto.FormulaValue,
            IsPercentage = dto.IsPercentage,
            TargetResource = dto.TargetResource,
            StatusId = dto.StatusId,
            StatusStacks = dto.StatusStacks,
            StatusDuration = dto.StatusDuration,
            ModifierKey = dto.ModifierKey,
            ModifierValue = dto.ModifierValue,
            ModifierFormula = dto.ModifierFormula,
            Condition = dto.Condition,
            RequiredTags = dto.RequiredTags,
            ExcludedTags = dto.ExcludedTags,
            Chance = dto.Chance,
            Repeat = dto.Repeat,
            Tags = dto.Tags,
            Metadata = dto.Metadata,
            ChainedEffects = dto.ChainedEffects?.Select(MapEffectFromDto).ToList(),
            ConditionalEffects = dto.ConditionalEffects?.Select(MapEffectFromDto).ToList()
        };
    }

    private sealed record ContextBuildResult(IEffectContext? Context, IActionResult? ActionResult)
    {
        public static ContextBuildResult Success(IEffectContext context) => new(context, null);
        public static ContextBuildResult Failure(IActionResult result) => new(null, result);
    }
}
