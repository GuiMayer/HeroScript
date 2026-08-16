using Microsoft.AspNetCore.Mvc;
using Core.StatusEffects;
using API.Models.StatusEffects;

namespace API.Controllers;

/// <summary>
/// Controller para gerenciamento de status effects.
/// </summary>
[ApiController]
[Route("api/status")]
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
        return ApplyStatusInternal(request);
    }

    private IActionResult ApplyStatusInternal(ApplyStatusRequest request)
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
        return RemoveStatusInternal(request);
    }

    private IActionResult RemoveStatusInternal(RemoveStatusRequest request)
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
    public IActionResult RemoveStatusByStatusId(string targetId, string statusId)
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
    public IActionResult RemoveAllStatus(string targetId, [FromQuery] string? type = null)
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
        return AddStacksInternal(request);
    }

    private IActionResult AddStacksInternal(ModifyStacksRequest request)
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
        return RemoveStacksInternal(request);
    }

    private IActionResult RemoveStacksInternal(ModifyStacksRequest request)
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
    public IActionResult RefreshDuration(string targetId, Guid instanceId, [FromBody] int duration)
    {
        return RefreshDurationInternal(targetId, instanceId, duration);
    }

    private IActionResult RefreshDurationInternal(string targetId, Guid instanceId, int duration)
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
    /// Obtém todos os status effects ativos de uma entidade (alias curto)
    /// </summary>
    [HttpGet("{targetId}")]
    public IActionResult GetStatusEffects(string targetId)
    {
        return GetActiveStatusInternal(targetId);
    }

    /// <summary>
    /// Obtém todos os status effects ativos de uma entidade
    /// </summary>
    [HttpGet("{targetId}/active")]
    public IActionResult GetActiveStatus(string targetId)
    {
        return GetActiveStatusInternal(targetId);
    }

    private IActionResult GetActiveStatusInternal(string targetId)
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
    public IActionResult GetStatus(string targetId, Guid instanceId)
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
    public IActionResult HasStatus(string targetId, string type)
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
    public IActionResult GetStatusStacks(string targetId, string type)
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
    public IActionResult TickDurations(string targetId)
    {
        return TickDurationsInternal(targetId);
    }

    private IActionResult TickDurationsInternal(string targetId)
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
    public IActionResult GetPipelineModifiers(string targetId)
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

    /// <summary>
    /// Cria uma nova definição de status effect
    /// </summary>
    [HttpPost("definitions")]
    [API.Attributes.AdminEndpoint]
    [ProducesResponseType(typeof(StatusEffectDefinitionDto), 201)]
    [ProducesResponseType(400)]
    [ProducesResponseType(500)]
    public IActionResult CreateDefinition([FromBody] StatusEffectDefinitionDto dto, [FromQuery] string configName = "default")
    {
        // Sanitize path traversal
        if (!API.Helpers.ValidationHelper.IsValidConfigName(configName))
            return BadRequest(new { error = "Invalid configuration name" });

        if (dto == null)
            return BadRequest(new { error = "Status effect definition cannot be null" });

        if (string.IsNullOrWhiteSpace(dto.StatusId))
            return BadRequest(new { error = "StatusId is required" });

        try
        {
            var definition = MapFromDto(dto);
            var result = _statusEffectManager.SaveDefinition(definition, configName);

            if (result.IsFailure)
                return BadRequest(new { error = result.Error });

            _logger.LogInformation($"Created status effect definition: {dto.StatusId}");
            return CreatedAtAction(nameof(GetDefinition), new { statusId = dto.StatusId }, dto);
        }
        catch (Exception ex)
        {
            return HandleException(ex, "create status effect definition", dto.StatusId);
        }
    }

    /// <summary>
    /// Atualiza uma definição de status effect existente
    /// </summary>
    [HttpPut("definitions/{statusId}")]
    [API.Attributes.AdminEndpoint]
    [ProducesResponseType(typeof(StatusEffectDefinitionDto), 200)]
    [ProducesResponseType(400)]
    [ProducesResponseType(404)]
    [ProducesResponseType(500)]
    public IActionResult UpdateDefinition(string statusId, [FromBody] StatusEffectDefinitionDto dto, [FromQuery] string configName = "default")
    {
        // Sanitize path traversal
        if (!API.Helpers.ValidationHelper.IsValidConfigName(configName))
            return BadRequest(new { error = "Invalid configuration name" });

        if (string.IsNullOrWhiteSpace(statusId))
            return BadRequest(new { error = "StatusId cannot be empty" });

        if (dto == null)
            return BadRequest(new { error = "Status effect definition cannot be null" });

        if (dto.StatusId != statusId)
            return BadRequest(new { error = $"StatusId mismatch: URL has '{statusId}' but body has '{dto.StatusId}'" });

        try
        {
            var definition = MapFromDto(dto);
            var result = _statusEffectManager.UpdateDefinition(statusId, definition, configName);

            if (result.IsFailure)
            {
                if (result.Error.Contains("not found"))
                    return NotFound(new { error = result.Error });
                return BadRequest(new { error = result.Error });
            }

            _logger.LogInformation($"Updated status effect definition: {statusId}");
            return Ok(dto);
        }
        catch (Exception ex)
        {
            return HandleException(ex, "update status effect definition", statusId);
        }
    }

    /// <summary>
    /// Deleta uma definição de status effect
    /// </summary>
    [HttpDelete("definitions/{statusId}")]
    [API.Attributes.AdminEndpoint]
    [ProducesResponseType(204)]
    [ProducesResponseType(400)]
    [ProducesResponseType(404)]
    [ProducesResponseType(500)]
    public IActionResult DeleteDefinition(string statusId, [FromQuery] string configName = "default")
    {
        // Sanitize path traversal
        if (!API.Helpers.ValidationHelper.IsValidConfigName(configName))
            return BadRequest(new { error = "Invalid configuration name" });

        if (string.IsNullOrWhiteSpace(statusId))
            return BadRequest(new { error = "StatusId cannot be empty" });

        try
        {
            var result = _statusEffectManager.DeleteDefinition(statusId, configName);

            if (result.IsFailure)
            {
                if (result.Error.Contains("not found"))
                    return NotFound(new { error = result.Error });
                return BadRequest(new { error = result.Error });
            }

            _logger.LogInformation($"Deleted status effect definition: {statusId}");
            return NoContent();
        }
        catch (Exception ex)
        {
            return HandleException(ex, "delete status effect definition", statusId);
        }
    }

    /// <summary>
    /// Obtém uma definição de status effect específica
    /// </summary>
    [HttpGet("definitions/{statusId}")]
    [ProducesResponseType(typeof(StatusEffectDefinitionDto), 200)]
    [ProducesResponseType(404)]
    [ProducesResponseType(500)]
    public IActionResult GetDefinition(string statusId)
    {
        try
        {
            var result = _statusEffectManager.GetDefinition(statusId);

            if (result.IsFailure)
                return NotFound(new { error = result.Error });

            var dto = MapToDto(result.Value);
            return Ok(dto);
        }
        catch (Exception ex)
        {
            return HandleException(ex, "get status effect definition", statusId);
        }
    }

    // Métodos de mapeamento
    private StatusEffectDefinition MapFromDto(StatusEffectDefinitionDto dto)
    {
        return new StatusEffectDefinition
        {
            StatusId = dto.StatusId,
            DisplayName = dto.DisplayName,
            Description = dto.Description,
            Type = Enum.TryParse<StatusEffectType>(dto.Type, true, out var type) 
                ? type 
                : StatusEffectType.CUSTOM,
            MaxStacks = dto.MaxStacks,
            DefaultDuration = dto.DefaultDuration,
            DefaultStacks = dto.DefaultStacks,
            BaseValue = dto.BaseValue,
            FormulaValue = dto.FormulaValue,
            ScalesWithStacks = dto.ScalesWithStacks,
            ModifierKey = dto.ModifierKey,
            ModifierFormula = dto.ModifierFormula,
            IconPath = dto.IconPath ?? string.Empty,
            Color = dto.Color,
            Tags = dto.Tags ?? new List<string>(),
            CustomData = dto.CustomData ?? new Dictionary<string, object>()
        };
    }

    private StatusEffectDefinitionDto MapToDto(StatusEffectDefinition definition)
    {
        return new StatusEffectDefinitionDto
        {
            StatusId = definition.StatusId,
            DisplayName = definition.DisplayName,
            Description = definition.Description,
            Type = definition.Type.ToString(),
            MaxStacks = definition.MaxStacks,
            DefaultDuration = definition.DefaultDuration,
            DefaultStacks = definition.DefaultStacks,
            BaseValue = definition.BaseValue,
            FormulaValue = definition.FormulaValue,
            ScalesWithStacks = definition.ScalesWithStacks,
            ModifierKey = definition.ModifierKey,
            ModifierFormula = definition.ModifierFormula,
            IconPath = definition.IconPath,
            Color = definition.Color,
            Tags = definition.Tags.ToList(),
            CustomData = definition.CustomData.ToDictionary(item => item.Key, item => item.Value)
        };
    }
}
