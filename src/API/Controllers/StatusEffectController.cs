using Microsoft.AspNetCore.Mvc;
using Core.StatusEffects;
using API.Models.StatusEffects;

namespace API.Controllers;

/// <summary>
/// Controller para gerenciamento de status effects.
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class StatusEffectController : BaseApiController
{
    private readonly IStatusEffectManager _statusEffectManager;

    public StatusEffectController(
        IStatusEffectManager statusEffectManager,
        ILogger<StatusEffectController> logger)
        : base(logger)
    {
        _statusEffectManager = statusEffectManager ?? throw new ArgumentNullException(nameof(statusEffectManager));
    }

    /// <summary>
    /// Aplica um status effect a uma entidade
    /// </summary>
    [HttpPost("apply")]
    public IActionResult ApplyStatus([FromBody] ApplyStatusRequest request)
    {
        try
        {
            var result = _statusEffectManager.ApplyStatus(
                request.TargetId,
                request.StatusId,
                request.Stacks,
                request.Duration,
                request.SourceId);

            if (result.IsFailure)
                return BadRequest(new { error = result.Error });

            return Ok(StatusEffectResponse.FromInstance(result.Value));
        }
        catch (Exception ex)
        {
            return HandleException(ex, "apply status effect", request.StatusId);
        }
    }

    /// <summary>
    /// Remove uma instância específica de status effect
    /// </summary>
    [HttpDelete("remove")]
    public IActionResult RemoveStatus([FromBody] RemoveStatusRequest request)
    {
        try
        {
            var result = _statusEffectManager.RemoveStatus(request.TargetId, request.InstanceId);

            if (result.IsFailure)
                return BadRequest(new { error = result.Error });

            return Ok(new { message = "Status effect removed successfully" });
        }
        catch (Exception ex)
        {
            return HandleException(ex, "remove status effect", request.InstanceId.ToString());
        }
    }

    /// <summary>
    /// Remove todos os status effects de um statusId específico
    /// </summary>
    [HttpDelete("{targetId}/status/{statusId}")]
    public IActionResult RemoveStatusByStatusId(Guid targetId, string statusId)
    {
        try
        {
            var result = _statusEffectManager.RemoveStatusByStatusId(targetId, statusId);

            if (result.IsFailure)
                return BadRequest(new { error = result.Error });

            return Ok(new { message = $"All instances of {statusId} removed successfully" });
        }
        catch (Exception ex)
        {
            return HandleException(ex, "remove status by statusId", statusId);
        }
    }

    /// <summary>
    /// Remove todos os status effects de uma entidade (opcionalmente filtrado por tipo)
    /// </summary>
    [HttpDelete("{targetId}/all")]
    public IActionResult RemoveAllStatus(Guid targetId, [FromQuery] string? type = null)
    {
        try
        {
            StatusEffectType? statusType = null;
            if (!string.IsNullOrEmpty(type))
            {
                if (!Enum.TryParse<StatusEffectType>(type, true, out var parsedType))
                    return BadRequest(new { error = $"Invalid status effect type: {type}" });
                statusType = parsedType;
            }

            var result = _statusEffectManager.RemoveAllStatus(targetId, statusType);

            if (result.IsFailure)
                return BadRequest(new { error = result.Error });

            return Ok(new { message = "Status effects removed successfully" });
        }
        catch (Exception ex)
        {
            return HandleException(ex, "remove all status effects", targetId.ToString());
        }
    }

    /// <summary>
    /// Adiciona stacks a um status effect existente
    /// </summary>
    [HttpPost("add-stacks")]
    public IActionResult AddStacks([FromBody] ModifyStacksRequest request)
    {
        try
        {
            var result = _statusEffectManager.AddStacks(
                request.TargetId,
                request.InstanceId,
                request.Stacks);

            if (result.IsFailure)
                return BadRequest(new { error = result.Error });

            return Ok(StatusEffectResponse.FromInstance(result.Value));
        }
        catch (Exception ex)
        {
            return HandleException(ex, "add stacks", request.InstanceId.ToString());
        }
    }

    /// <summary>
    /// Remove stacks de um status effect existente
    /// </summary>
    [HttpPost("remove-stacks")]
    public IActionResult RemoveStacks([FromBody] ModifyStacksRequest request)
    {
        try
        {
            var result = _statusEffectManager.RemoveStacks(
                request.TargetId,
                request.InstanceId,
                request.Stacks);

            if (result.IsFailure)
                return BadRequest(new { error = result.Error });

            if (result.Value == null)
                return Ok(new { message = "Status effect removed (stacks reached 0)" });

            return Ok(StatusEffectResponse.FromInstance(result.Value));
        }
        catch (Exception ex)
        {
            return HandleException(ex, "remove stacks", request.InstanceId.ToString());
        }
    }

    /// <summary>
    /// Atualiza a duração de um status effect
    /// </summary>
    [HttpPut("{targetId}/status/{instanceId}/duration")]
    public IActionResult RefreshDuration(Guid targetId, Guid instanceId, [FromBody] int duration)
    {
        try
        {
            var result = _statusEffectManager.RefreshDuration(targetId, instanceId, duration);

            if (result.IsFailure)
                return BadRequest(new { error = result.Error });

            return Ok(StatusEffectResponse.FromInstance(result.Value));
        }
        catch (Exception ex)
        {
            return HandleException(ex, "refresh duration", instanceId.ToString());
        }
    }

    /// <summary>
    /// Obtém todos os status effects ativos de uma entidade
    /// </summary>
    [HttpGet("{targetId}/active")]
    public IActionResult GetActiveStatus(Guid targetId)
    {
        try
        {
            var result = _statusEffectManager.GetActiveStatus(targetId);

            if (result.IsFailure)
                return BadRequest(new { error = result.Error });

            var response = result.Value.Select(StatusEffectResponse.FromInstance).ToList();
            return Ok(response);
        }
        catch (Exception ex)
        {
            return HandleException(ex, "get active status effects", targetId.ToString());
        }
    }

    /// <summary>
    /// Obtém uma instância específica de status effect
    /// </summary>
    [HttpGet("{targetId}/status/{instanceId}")]
    public IActionResult GetStatus(Guid targetId, Guid instanceId)
    {
        try
        {
            var result = _statusEffectManager.GetStatus(targetId, instanceId);

            if (result.IsFailure)
                return NotFound(new { error = result.Error });

            return Ok(StatusEffectResponse.FromInstance(result.Value));
        }
        catch (Exception ex)
        {
            return HandleException(ex, "get status effect", instanceId.ToString());
        }
    }

    /// <summary>
    /// Verifica se uma entidade possui um status effect de um tipo específico
    /// </summary>
    [HttpGet("{targetId}/has/{type}")]
    public IActionResult HasStatus(Guid targetId, string type)
    {
        try
        {
            if (!Enum.TryParse<StatusEffectType>(type, true, out var statusType))
                return BadRequest(new { error = $"Invalid status effect type: {type}" });

            var hasStatus = _statusEffectManager.HasStatus(targetId, statusType);
            return Ok(new { hasStatus });
        }
        catch (Exception ex)
        {
            return HandleException(ex, "check status effect", type);
        }
    }

    /// <summary>
    /// Obtém o número total de stacks de um tipo de status
    /// </summary>
    [HttpGet("{targetId}/stacks/{type}")]
    public IActionResult GetStatusStacks(Guid targetId, string type)
    {
        try
        {
            if (!Enum.TryParse<StatusEffectType>(type, true, out var statusType))
                return BadRequest(new { error = $"Invalid status effect type: {type}" });

            var stacks = _statusEffectManager.GetStatusStacks(targetId, statusType);
            return Ok(new { stacks });
        }
        catch (Exception ex)
        {
            return HandleException(ex, "get status stacks", type);
        }
    }

    /// <summary>
    /// Processa todos os status effects de uma entidade com um timing específico
    /// </summary>
    [HttpPost("process")]
    public IActionResult ProcessStatusEffects([FromBody] ProcessStatusEffectsRequest request)
    {
        try
        {
            if (!Enum.TryParse<StatusEffectTiming>(request.Timing, true, out var timing))
                return BadRequest(new { error = $"Invalid timing: {request.Timing}" });

            var result = _statusEffectManager.ProcessStatusEffects(
                request.TargetId,
                timing,
                request.CurrentTurn);

            if (result.IsFailure)
                return BadRequest(new { error = result.Error });

            return Ok(ProcessStatusEffectsResponse.FromProcessResult(result.Value));
        }
        catch (Exception ex)
        {
            return HandleException(ex, "process status effects", request.TargetId.ToString());
        }
    }

    /// <summary>
    /// Decrementa a duração de todos os status effects de uma entidade
    /// </summary>
    [HttpPost("{targetId}/tick")]
    public IActionResult TickDurations(Guid targetId)
    {
        try
        {
            var result = _statusEffectManager.TickDurations(targetId);

            if (result.IsFailure)
                return BadRequest(new { error = result.Error });

            return Ok(new { message = "Durations decremented successfully" });
        }
        catch (Exception ex)
        {
            return HandleException(ex, "tick durations", targetId.ToString());
        }
    }

    /// <summary>
    /// Obtém modificadores de pipeline de uma entidade
    /// </summary>
    [HttpGet("{targetId}/modifiers")]
    public IActionResult GetPipelineModifiers(Guid targetId)
    {
        try
        {
            var modifiers = _statusEffectManager.GetPipelineModifiers(targetId);
            return Ok(modifiers);
        }
        catch (Exception ex)
        {
            return HandleException(ex, "get pipeline modifiers", targetId.ToString());
        }
    }
}
