using System.Text.Json;
using Core.Combat.Models;
using Core.Common;
using Core.Logging;

namespace Core.Combat.TurnPhase;

/// <summary>
/// Carregador de sequências de fases a partir de arquivos JSON.
/// Permite configurar diferentes estilos de TCG (Magic, Yu-Gi-Oh!, Hearthstone, etc.)
/// </summary>
public class PhaseSequenceLoader
{
    private readonly ILogger _logger;
    private readonly Dictionary<string, PhaseSequenceDefinition> _cache;
    
    public PhaseSequenceLoader(ILogger logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _cache = new Dictionary<string, PhaseSequenceDefinition>();
    }
    
    /// <summary>
    /// Carrega uma sequência de fases a partir de um arquivo JSON.
    /// </summary>
    /// <param name="filePath">Caminho para o arquivo JSON</param>
    /// <param name="useCache">Se true, usa cache se disponível</param>
    /// <returns>Definição da sequência de fases</returns>
    public Result<PhaseSequenceDefinition> LoadFromFile(string filePath, bool useCache = true)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return Result<PhaseSequenceDefinition>.Failure("File path cannot be empty");
        }
        
        // Verificar cache
        if (useCache && _cache.TryGetValue(filePath, out var cached))
        {
            _logger.LogDebug($"Loaded phase sequence from cache: {filePath}");
            return Result<PhaseSequenceDefinition>.Success(cached);
        }
        
        // Verificar se arquivo existe
        if (!File.Exists(filePath))
        {
            return Result<PhaseSequenceDefinition>.Failure($"File not found: {filePath}");
        }
        
        try
        {
            // Ler arquivo JSON
            var json = File.ReadAllText(filePath);
            
            // Deserializar
            var dto = JsonSerializer.Deserialize<PhaseSequenceDto>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                ReadCommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true
            });
            
            if (dto == null)
            {
                return Result<PhaseSequenceDefinition>.Failure("Failed to deserialize JSON");
            }
            
            // Converter DTO para record
            var result = ConvertDtoToDefinition(dto);
            if (result.IsFailure)
            {
                return result;
            }
            
            // Validar
            var validationResult = ValidateSequence(result.Value);
            if (validationResult.IsFailure)
            {
                return Result<PhaseSequenceDefinition>.Failure($"Validation failed: {validationResult.Error}");
            }
            
            // Adicionar ao cache
            _cache[filePath] = result.Value;
            
            _logger.LogInformation($"Loaded phase sequence: {result.Value.Name} from {filePath}");
            
            return result;
        }
        catch (JsonException ex)
        {
            return Result<PhaseSequenceDefinition>.Failure($"JSON parsing error: {ex.Message}");
        }
        catch (Exception ex)
        {
            return Result<PhaseSequenceDefinition>.Failure($"Error loading file: {ex.Message}");
        }
    }
    
    /// <summary>
    /// Carrega uma sequência de fases a partir de uma string JSON.
    /// </summary>
    /// <param name="json">String JSON</param>
    /// <returns>Definição da sequência de fases</returns>
    public Result<PhaseSequenceDefinition> LoadFromJson(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return Result<PhaseSequenceDefinition>.Failure("JSON string cannot be empty");
        }
        
        try
        {
            // Deserializar
            var dto = JsonSerializer.Deserialize<PhaseSequenceDto>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                ReadCommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true
            });
            
            if (dto == null)
            {
                return Result<PhaseSequenceDefinition>.Failure("Failed to deserialize JSON");
            }
            
            // Converter DTO para record
            var result = ConvertDtoToDefinition(dto);
            if (result.IsFailure)
            {
                return result;
            }
            
            // Validar
            var validationResult = ValidateSequence(result.Value);
            if (validationResult.IsFailure)
            {
                return Result<PhaseSequenceDefinition>.Failure($"Validation failed: {validationResult.Error}");
            }
            
            _logger.LogInformation($"Loaded phase sequence from JSON: {result.Value.Name}");
            
            return result;
        }
        catch (JsonException ex)
        {
            return Result<PhaseSequenceDefinition>.Failure($"JSON parsing error: {ex.Message}");
        }
        catch (Exception ex)
        {
            return Result<PhaseSequenceDefinition>.Failure($"Error parsing JSON: {ex.Message}");
        }
    }
    
    /// <summary>
    /// Limpa o cache de sequências carregadas.
    /// </summary>
    public void ClearCache()
    {
        _cache.Clear();
        _logger.LogDebug("Phase sequence cache cleared");
    }
    
    private Result<PhaseSequenceDefinition> ConvertDtoToDefinition(PhaseSequenceDto dto)
    {
        try
        {
            // Converter strings de fase para enum
            var phases = new List<TurnPhase>();
            foreach (var phaseStr in dto.Phases)
            {
                if (!Enum.TryParse<TurnPhase>(phaseStr, true, out var phase))
                {
                    return Result<PhaseSequenceDefinition>.Failure($"Invalid phase name: {phaseStr}");
                }
                phases.Add(phase);
            }
            
            // Converter detalhes de fase
            var phaseDetails = new Dictionary<TurnPhase, PhaseDefinition>();
            foreach (var kvp in dto.PhaseDetails)
            {
                if (!Enum.TryParse<TurnPhase>(kvp.Key, true, out var phase))
                {
                    return Result<PhaseSequenceDefinition>.Failure($"Invalid phase name in details: {kvp.Key}");
                }
                
                var detailDto = kvp.Value;
                
                // Converter ações permitidas
                var allowedActions = new List<ActionType>();
                foreach (var actionStr in detailDto.AllowedActions)
                {
                    if (!Enum.TryParse<ActionType>(actionStr, true, out var action))
                    {
                        return Result<PhaseSequenceDefinition>.Failure($"Invalid action type: {actionStr}");
                    }
                    allowedActions.Add(action);
                }
                
                // Converter próximas fases válidas
                var validNextPhases = new List<TurnPhase>();
                foreach (var nextPhaseStr in detailDto.ValidNextPhases)
                {
                    if (!Enum.TryParse<TurnPhase>(nextPhaseStr, true, out var nextPhase))
                    {
                        return Result<PhaseSequenceDefinition>.Failure($"Invalid next phase: {nextPhaseStr}");
                    }
                    validNextPhases.Add(nextPhase);
                }
                
                var definition = new PhaseDefinition
                {
                    Name = detailDto.Name,
                    Description = detailDto.Description,
                    AllowedActions = allowedActions,
                    ValidNextPhases = validNextPhases,
                    AutoTransition = detailDto.AutoTransition,
                    AllowPriority = detailDto.AllowPriority
                };
                
                phaseDetails[phase] = definition;
            }
            
            var sequence = new PhaseSequenceDefinition
            {
                Name = dto.Name,
                Description = dto.Description,
                Version = dto.Version,
                Phases = phases,
                PhaseDetails = phaseDetails,
                AllowPhaseSkipping = dto.AllowPhaseSkipping
            };
            
            return Result<PhaseSequenceDefinition>.Success(sequence);
        }
        catch (Exception ex)
        {
            return Result<PhaseSequenceDefinition>.Failure($"Error converting DTO: {ex.Message}");
        }
    }
    
    private Result ValidateSequence(PhaseSequenceDefinition sequence)
    {
        // Validar que há pelo menos uma fase
        if (sequence.Phases.Count == 0)
        {
            return Result.Failure("Sequence must have at least one phase");
        }
        
        // Validar que todas as fases têm detalhes
        foreach (var phase in sequence.Phases)
        {
            if (!sequence.PhaseDetails.ContainsKey(phase))
            {
                return Result.Failure($"Missing phase details for: {phase}");
            }
        }
        
        // Validar que próximas fases válidas existem na sequência
        foreach (var kvp in sequence.PhaseDetails)
        {
            foreach (var nextPhase in kvp.Value.ValidNextPhases)
            {
                if (!sequence.Phases.Contains(nextPhase))
                {
                    return Result.Failure($"Phase {kvp.Key} references invalid next phase: {nextPhase}");
                }
            }
        }
        
        return Result.Success();
    }
    
    // DTOs para deserialização JSON
    private class PhaseSequenceDto
    {
        public string Name { get; set; } = "";
        public string Description { get; set; } = "";
        public string Version { get; set; } = "1.0.0";
        public List<string> Phases { get; set; } = new();
        public Dictionary<string, PhaseDefinitionDto> PhaseDetails { get; set; } = new();
        public bool AllowPhaseSkipping { get; set; } = false;
    }
    
    private class PhaseDefinitionDto
    {
        public string Name { get; set; } = "";
        public string Description { get; set; } = "";
        public List<string> AllowedActions { get; set; } = new();
        public List<string> ValidNextPhases { get; set; } = new();
        public bool AutoTransition { get; set; } = false;
        public bool AllowPriority { get; set; } = true;
    }
}
