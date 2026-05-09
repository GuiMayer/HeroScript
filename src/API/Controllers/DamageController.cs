using API.Models;
using Core.Combat;
using Core.Damage;
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
    private readonly CoreLogger _logger;

    public DamageController(
        IDamageCalculator damageCalculator,
        IPipelineManager pipelineManager,
        CoreLogger logger)
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

    /// <summary>
    /// Cria entidade mock para cálculo de dano
    /// </summary>
    private CombatEntity CreateMockEntity(EntityStatsDto stats)
    {
        // Criar definição de recurso health mock
        var healthDef = new Core.Resources.ResourceDefinition
        {
            ResourceId = "health",
            DisplayName = "Health",
            Category = Core.Resources.ResourceCategory.VITAL,
            DefaultCurrent = 100,
            DefaultMax = 100,
            DefaultMin = 0,
            CanBeNegative = false,
            CanExceedMax = false,
            Tags = new List<string>()
        };
        
        // Criar pool de health usando record syntax
        var healthPool = new Core.Resources.ResourcePool
        {
            ResourceId = "health",
            Current = 100,
            Maximum = 100,
            Minimum = 0,
            Definition = healthDef
        };
        
        // Criar EntityResourceState usando record syntax
        var resourceState = new EntityResourceState
        {
            EntityId = stats.EntityId,
            Resources = new Dictionary<string, Core.Resources.ResourcePool> { ["health"] = healthPool }
        };
        
        // Criar entidade usando record syntax
        var entity = new CombatEntity
        {
            EntityId = stats.EntityId,
            Name = stats.EntityId,
            IsHero = false,
            ResourceState = resourceState
        };
        
        return entity;
    }
}
