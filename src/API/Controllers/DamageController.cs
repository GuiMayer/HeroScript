using API.Models;
using Core.Combat;
using Core.Damage;
using Core.Logging;
using Microsoft.AspNetCore.Mvc;

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
    private readonly ILogger _logger;

    public DamageController(
        IDamageCalculator damageCalculator,
        IPipelineManager pipelineManager,
        ILogger logger)
    {
        _damageCalculator = damageCalculator ?? throw new ArgumentNullException(nameof(damageCalculator));
        _pipelineManager = pipelineManager ?? throw new ArgumentNullException(nameof(pipelineManager));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Calcula dano de uma ação
    /// </summary>
    /// <param name="request">Dados da ação e entidades</param>
    /// <returns>Resultado do cálculo com breakdown</returns>
    [HttpPost("calculate")]
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
                BaseDamage = request.BaseDamage,
                Tags = request.Tags
            };
            
            // Criar entidades mock (em produção viriam do CombatSystem)
            var attacker = CreateMockEntity(request.Attacker);
            var target = CreateMockEntity(request.Target);
            
            // Calcular dano
            var result = _damageCalculator.CalculateDamage(actionDef, attacker, target);
            
            // Montar response
            var response = new CalculateDamageResponse
            {
                FinalDamage = result.FinalDamage,
                CritTier = result.CritTier,
                BaseDamage = request.BaseDamage,
                Metadata = result.Metadata,
                Breakdown = new List<BucketBreakdownDto>() // TODO: Capturar breakdown dos eventos
            };
            
            _logger.LogInfo($"Damage calculated via API: {request.ActionId} -> {result.FinalDamage:F2}");
            
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
                    Description = b.Description,
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
                    Description = b.Description,
                    OperationCount = b.Operations.Count,
                    EmitEvents = b.EmitEvents
                }).ToList()
            };
            
            _logger.LogInfo("Pipeline reloaded via API");
            
            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error reloading pipeline: {ex.Message}");
            return BadRequest(ex.Message);
        }
    }

    /// <summary>
    /// Cria entidade mock para cálculo de dano
    /// </summary>
    private CombatEntity CreateMockEntity(EntityStatsDto stats)
    {
        // Criar pools de recursos mock
        var healthPool = new Core.Resources.ResourcePool("health", 100, 100, 0, 100);
        
        var entity = new CombatEntity(
            stats.EntityId,
            stats.EntityId,
            new Dictionary<string, Core.Resources.ResourcePool> { ["health"] = healthPool }
        );
        
        // Aplicar stats (em produção isso viria do sistema de stats)
        // Por enquanto, apenas retornar a entidade base
        // TODO: Implementar sistema de stats/modifiers
        
        return entity;
    }
}
