using System;
using System.Collections.Generic;
using System.Linq;

namespace Core.Damage;

/// <summary>
/// Configuração completa do pipeline de dano (carregada de JSON).
/// Para estrutura do JSON e exemplos, veja: src/Core/Damage/README.md
/// </summary>
public record PipelineConfiguration
{
    /// <summary>
    /// Nome da configuração (ex: "default", "high-crit")
    /// </summary>
    public string ConfigName { get; init; } = string.Empty;
    
    /// <summary>
    /// Lista de buckets do pipeline
    /// </summary>
    public List<BucketDefinition> Buckets { get; init; } = new();
    
    /// <summary>
    /// Valida a configuração do pipeline
    /// </summary>
    /// <exception cref="InvalidOperationException">Lançada quando a configuração é inválida</exception>
    public bool Validate()
    {
        // Verificar IDs únicos
        var duplicateIds = Buckets
            .GroupBy(b => b.BucketId)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();
        
        if (duplicateIds.Any())
            throw new InvalidOperationException($"Duplicate bucket IDs: {string.Join(", ", duplicateIds)}");
        
        // Verificar ordem válida (sem gaps, sem duplicatas)
        var orders = Buckets.Select(b => b.Order).OrderBy(o => o).ToList();
        for (int i = 0; i < orders.Count; i++)
        {
            if (orders[i] != i + 1)
                throw new InvalidOperationException($"Invalid bucket order: expected {i + 1}, got {orders[i]}");
        }
        
        // Verificar que há pelo menos um bucket
        if (Buckets.Count == 0)
            throw new InvalidOperationException("Pipeline must have at least one bucket");
        
        return true;
    }
}
