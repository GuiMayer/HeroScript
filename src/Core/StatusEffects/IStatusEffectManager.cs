using Core.Common;
using System.Collections.Immutable;

namespace Core.StatusEffects;

/// <summary>
/// Interface para gerenciamento de status effects.
/// Responsável por aplicar, remover, consultar e processar status effects em entidades.
/// </summary>
public interface IStatusEffectManager
{
    // ===== APLICAR/REMOVER =====
    
    /// <summary>
    /// Aplica um status effect a uma entidade
    /// </summary>
    /// <param name="targetId">ID da entidade alvo</param>
    /// <param name="statusId">ID do status effect</param>
    /// <param name="stacks">Número de stacks (padrão: 1)</param>
    /// <param name="duration">Duração em turnos (null = usar padrão da definição)</param>
    /// <param name="sourceId">ID da entidade que aplicou (opcional)</param>
    /// <returns>Instância do status effect aplicado</returns>
    Result<StatusEffectInstance> ApplyStatus(
        Guid targetId,
        string statusId,
        int stacks = 1,
        int? duration = null,
        Guid? sourceId = null);

    /// <summary>
    /// Aplica um status com identidade e tempo lógico fornecidos pela transição.
    /// Este é o contrato usado por runs determinísticas.
    /// </summary>
    Result<StatusEffectInstance> ApplyStatus(
        Guid targetId,
        string statusId,
        Guid instanceId,
        DateTime appliedAt,
        int stacks = 1,
        int? duration = null,
        Guid? sourceId = null);
    
    /// <summary>
    /// Remove uma instância específica de status effect
    /// </summary>
    /// <param name="targetId">ID da entidade alvo</param>
    /// <param name="instanceId">ID da instância do status</param>
    /// <returns>Resultado da operação</returns>
    Result RemoveStatus(Guid targetId, Guid instanceId);
    
    /// <summary>
    /// Remove todos os status effects com um statusId específico
    /// </summary>
    /// <param name="targetId">ID da entidade alvo</param>
    /// <param name="statusId">ID do status a remover</param>
    /// <returns>Resultado da operação</returns>
    Result RemoveStatusByStatusId(Guid targetId, string statusId);
    
    /// <summary>
    /// Remove todos os status effects de um tipo específico
    /// </summary>
    /// <param name="targetId">ID da entidade alvo</param>
    /// <param name="type">Tipo de status a remover (null = todos)</param>
    /// <returns>Resultado da operação</returns>
    Result RemoveAllStatus(Guid targetId, StatusEffectType? type = null);
    
    // ===== MODIFICAR =====
    
    /// <summary>
    /// Adiciona stacks a um status effect existente
    /// </summary>
    /// <param name="targetId">ID da entidade alvo</param>
    /// <param name="instanceId">ID da instância do status</param>
    /// <param name="stacks">Número de stacks a adicionar</param>
    /// <returns>Instância atualizada</returns>
    Result<StatusEffectInstance> AddStacks(Guid targetId, Guid instanceId, int stacks);
    
    /// <summary>
    /// Remove stacks de um status effect existente
    /// Se stacks chegar a 0, remove o status
    /// </summary>
    /// <param name="targetId">ID da entidade alvo</param>
    /// <param name="instanceId">ID da instância do status</param>
    /// <param name="stacks">Número de stacks a remover</param>
    /// <returns>Instância atualizada (ou null se removido)</returns>
    Result<StatusEffectInstance?> RemoveStacks(Guid targetId, Guid instanceId, int stacks);
    
    /// <summary>
    /// Atualiza a duração de um status effect
    /// </summary>
    /// <param name="targetId">ID da entidade alvo</param>
    /// <param name="instanceId">ID da instância do status</param>
    /// <param name="duration">Nova duração em turnos</param>
    /// <returns>Instância atualizada</returns>
    Result<StatusEffectInstance> RefreshDuration(Guid targetId, Guid instanceId, int duration);
    
    // ===== CONSULTAR =====
    
    /// <summary>
    /// Obtém todos os status effects ativos de uma entidade
    /// </summary>
    /// <param name="targetId">ID da entidade alvo</param>
    /// <returns>Lista de status effects ativos</returns>
    Result<List<StatusEffectInstance>> GetActiveStatus(Guid targetId);
    
    /// <summary>
    /// Obtém uma instância específica de status effect
    /// </summary>
    /// <param name="targetId">ID da entidade alvo</param>
    /// <param name="instanceId">ID da instância do status</param>
    /// <returns>Instância do status effect</returns>
    Result<StatusEffectInstance> GetStatus(Guid targetId, Guid instanceId);
    
    /// <summary>
    /// Verifica se uma entidade possui um status effect de um tipo específico
    /// </summary>
    /// <param name="targetId">ID da entidade alvo</param>
    /// <param name="type">Tipo de status</param>
    /// <returns>True se possui o status</returns>
    bool HasStatus(Guid targetId, StatusEffectType type);
    
    /// <summary>
    /// Obtém o número total de stacks de um tipo de status
    /// </summary>
    /// <param name="targetId">ID da entidade alvo</param>
    /// <param name="type">Tipo de status</param>
    /// <returns>Número total de stacks</returns>
    int GetStatusStacks(Guid targetId, StatusEffectType type);
    
    // ===== PROCESSAR =====
    
    /// <summary>
    /// Processa todos os status effects de uma entidade com um timing específico
    /// Ex: Processar DoTs no fim do turno
    /// </summary>
    /// <param name="targetId">ID da entidade alvo</param>
    /// <param name="timing">Timing de processamento</param>
    /// <param name="currentTurn">Turno atual</param>
    /// <returns>Resultado do processamento</returns>
    Result<StatusEffectProcessResult> ProcessStatusEffects(
        Guid targetId,
        StatusEffectTiming timing,
        int currentTurn);
    
    /// <summary>
    /// Decrementa a duração de todos os status effects de uma entidade
    /// Remove status effects que expiraram (duração = 0)
    /// </summary>
    /// <param name="targetId">ID da entidade alvo</param>
    /// <returns>Resultado da operação</returns>
    Result TickDurations(Guid targetId);
    
    /// <summary>
    /// Obtém modificadores de pipeline de uma entidade
    /// Ex: Strength adiciona "increased_damage_total"
    /// </summary>
    /// <param name="targetId">ID da entidade alvo</param>
    /// <returns>Dicionário de modificadores (chave -> valor)</returns>
    Dictionary<string, float> GetPipelineModifiers(Guid targetId);
    
    // ===== DEFINIÇÕES =====
    
    /// <summary>
    /// Carrega definições de status effects de um arquivo JSON
    /// </summary>
    /// <param name="configName">Nome da configuração (ex: "status_effects")</param>
    /// <returns>Resultado da operação</returns>
    Result LoadStatusDefinitions(string configName);
    
    /// <summary>
    /// Obtém a definição de um status effect
    /// </summary>
    /// <param name="statusId">ID do status effect</param>
    /// <returns>Definição do status effect</returns>
    Result<StatusEffectDefinition> GetDefinition(string statusId);
    
    /// <summary>
    /// Obtém todas as definições de status effects carregadas
    /// </summary>
    /// <returns>Lista de definições</returns>
    List<StatusEffectDefinition> GetAllDefinitions();
    
    /// <summary>
    /// Salva uma nova definição de status effect.
    /// </summary>
    /// <param name="definition">Definição do status effect</param>
    /// <param name="configName">Nome do config (padrão: "default")</param>
    /// <returns>Result indicando sucesso ou falha</returns>
    Result SaveDefinition(StatusEffectDefinition definition, string configName = "default");
    
    /// <summary>
    /// Atualiza uma definição de status effect existente.
    /// </summary>
    /// <param name="statusId">ID do status effect</param>
    /// <param name="updatedDefinition">Definição atualizada</param>
    /// <param name="configName">Nome do config (padrão: "default")</param>
    /// <returns>Result indicando sucesso ou falha</returns>
    Result UpdateDefinition(string statusId, StatusEffectDefinition updatedDefinition, string configName = "default");
    
    /// <summary>
    /// Deleta uma definição de status effect.
    /// </summary>
    /// <param name="statusId">ID do status effect</param>
    /// <param name="configName">Nome do config (padrão: "default")</param>
    /// <returns>Result indicando sucesso ou falha</returns>
    Result DeleteDefinition(string statusId, string configName = "default");
}

/// <summary>
/// Resultado do processamento de status effects
/// </summary>
public record StatusEffectProcessResult
{
    private ImmutableList<StatusEffectTickResult> _tickResults = [];
    private ImmutableList<StatusEffectInstance> _expiredStatus = [];

    /// <summary>
    /// ID da entidade processada
    /// </summary>
    public Guid TargetId { get; init; }
    
    /// <summary>
    /// Resultados individuais de cada status effect processado
    /// </summary>
    public IReadOnlyList<StatusEffectTickResult> TickResults
    {
        get => _tickResults;
        init => _tickResults = value?.ToImmutableList() ?? [];
    }
    
    /// <summary>
    /// Status effects que expiraram durante o processamento
    /// </summary>
    public IReadOnlyList<StatusEffectInstance> ExpiredStatus
    {
        get => _expiredStatus;
        init => _expiredStatus = value?.ToImmutableList() ?? [];
    }
}

/// <summary>
/// Resultado do processamento de um status effect individual
/// </summary>
public record StatusEffectTickResult
{
    /// <summary>
    /// ID da instância do status
    /// </summary>
    public Guid InstanceId { get; init; }
    
    /// <summary>
    /// ID do status effect
    /// </summary>
    public string StatusId { get; init; } = string.Empty;
    
    /// <summary>
    /// Tipo do status effect
    /// </summary>
    public StatusEffectType Type { get; init; }

    /// <summary>
    /// Comportamento configurado do status effect.
    /// Usado pelo motor para aplicar semântica sem depender de tipos específicos.
    /// </summary>
    public StatusEffectBehavior Behavior { get; init; }
    
    /// <summary>
    /// Valor aplicado (dano causado, cura aplicada, etc.)
    /// </summary>
    public float Value { get; init; }
    
    /// <summary>
    /// Se o efeito foi bloqueado (ex: por Artifact)
    /// </summary>
    public bool WasBlocked { get; init; }
    
    /// <summary>
    /// Mensagem descritiva do que aconteceu
    /// </summary>
    public string Message { get; init; } = string.Empty;
}
