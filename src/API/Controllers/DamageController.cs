using API.Models;
using Core.Combat;
using Core.Combat.Models;
using Core.Damage;
using Core.Effects;
using Microsoft.AspNetCore.Mvc;
using CoreLogger = Core.Logging.ILogger;

namespace API.Controllers;

/// <summary>
/// Controller para cálculo de dano e gerenciamento do pipeline
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class DamageController : ControllerBase
{
    private readonly IDamageCalculator _damageCalculator;
    private readonly IPipelineManager _pipelineManager;
    private readonly IEntityFactory _entityFactory;
    private readonly CoreLogger _logger;

    public DamageController(
        IDamageCalculator damageCalculator,
        IPipelineManager pipelineManager,
        IEntityFactory entityFactory,
        CoreLogger logger)
    {
        _damageCalculator = damageCalculator ?? throw new ArgumentNullException(nameof(damageCalculator));
        _pipelineManager = pipelineManager ?? throw new ArgumentNullException(nameof(pipelineManager));
        _entityFactory = entityFactory ?? throw new ArgumentNullException(nameof(entityFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Simula o cálculo de dano de uma ação.
    /// Este endpoint é diagnóstico; execução real de efeitos deve usar /api/effect/apply ou /api/combat/{id}/action.
    /// </summary>
    /// <param name="request">Dados da ação e entidades</param>
    /// <returns>Resultado do cálculo com breakdown</returns>
    [HttpPost("calculate")]
    [HttpPost("simulate")]
    [ProducesResponseType(typeof(CalculateDamageResponse), 200)]
    [ProducesResponseType(400)]
    public IActionResult CalculateDamage([FromBody] CalculateDamageRequest request)
    {
        try
        {
            // Validar request
            if (string.IsNullOrWhiteSpace(request.ActionId))
                return BadRequest("ActionId is required");
            
            if (string.IsNullOrWhiteSpace(request.Attacker.EntityId))
                return BadRequest("Attacker.EntityId is required");
            
            if (string.IsNullOrWhiteSpace(request.Target.EntityId))
                return BadRequest("Target.EntityId is required");
            
            // Criar ActionDefinition
            var actionDef = new ActionDefinition
            {
                ActionId = request.ActionId,
                Tags = request.Tags,
                Effects = new List<EffectDefinition>
                {
                    new EffectDefinition
                    {
                        Type = EffectType.DAMAGE,
                        FlatValue = request.BaseDamage,
                        Target = EffectTarget.TARGET
                    }
                }
            };
            
            // Criar entidades mock (em produção viriam do CombatSystem)
            var attackerResult = _entityFactory.CreateMockEntity(request.Attacker.EntityId);
            if (attackerResult.IsFailure)
                return BadRequest(new { error = $"Failed to create attacker: {attackerResult.Error}" });

            var targetResult = _entityFactory.CreateMockEntity(request.Target.EntityId);
            if (targetResult.IsFailure)
                return BadRequest(new { error = $"Failed to create target: {targetResult.Error}" });

            var attacker = attackerResult.Value;
            var target = targetResult.Value;
            
            // Calcular dano
            var result = _damageCalculator.CalculateDamage(actionDef, attacker, target);
            
            // Montar response
            var response = new CalculateDamageResponse
            {
                ActionId = request.ActionId,
                Mode = "simulation",
                FinalDamage = result.FinalDamage,
                CritTier = result.CritTier,
                BaseDamage = request.BaseDamage,
                Metadata = result.Metadata,
                Breakdown = new List<BucketBreakdownDto>() // TODO: Capturar breakdown dos eventos
            };
            
            _logger.LogDebug($"Damage calculated via API: {request.ActionId} -> {result.FinalDamage:F2}");
            
            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error calculating damage: {ex.Message}");
            return BadRequest(ex.Message);
        }
    }

    /// <summary>
    /// Obtém configuração atual do pipeline
    /// </summary>
    /// <returns>Configuração do pipeline</returns>
    [HttpGet("pipeline/config")]
    [ProducesResponseType(typeof(PipelineConfigResponse), 200)]
    public IActionResult GetPipelineConfig()
    {
        try
        {
            var config = _pipelineManager.GetCurrentConfiguration();
            
            var response = new PipelineConfigResponse
            {
                BucketCount = config.Buckets.Count,
                Buckets = config.Buckets.Select(b => new BucketInfoDto
                {
                    BucketId = b.BucketId,
                    Description = b.BucketId, // BucketDefinition doesn't have Description property
                    OperationCount = b.Operations.Count,
                    EmitEvents = b.EmitEvents
                }).ToList()
            };
            
            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error getting pipeline config: {ex.Message}");
            return BadRequest(ex.Message);
        }
    }

    /// <summary>
    /// Recarrega configuração do pipeline
    /// </summary>
    /// <returns>Nova configuração</returns>
    [HttpPost("pipeline/reload")]
    [API.Attributes.AdminEndpoint]
    [ProducesResponseType(typeof(PipelineConfigResponse), 200)]
    [ProducesResponseType(400)]
    public IActionResult ReloadPipeline()
    {
        try
        {
            // Recarregar com configuração padrão
            _pipelineManager.ReloadConfiguration(new[] { "DamagePipeline.json" });
            
            var config = _pipelineManager.GetCurrentConfiguration();
            
            var response = new PipelineConfigResponse
            {
                BucketCount = config.Buckets.Count,
                Buckets = config.Buckets.Select(b => new BucketInfoDto
                {
                    BucketId = b.BucketId,
                    Description = b.BucketId, // BucketDefinition doesn't have Description property
                    OperationCount = b.Operations.Count,
                    EmitEvents = b.EmitEvents
                }).ToList()
            };

            _logger.LogDebug("Pipeline reloaded via API");
            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error reloading pipeline: {ex.Message}");
            return BadRequest(ex.Message);
        }
    }
}
