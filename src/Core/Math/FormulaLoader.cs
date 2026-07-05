using System;
using System.Collections.Generic;
using System.Text.Json;
using Core.Logging;

namespace Core.Math
{
    /// <summary>
    /// Loader específico para fórmulas matemáticas.
    /// Usa o ResourceLoader genérico e converte JsonElement para FormulaDefinition.
    /// </summary>
    public class FormulaLoader
    {
        private readonly Config.IResourceLoader _resourceLoader;
        private readonly ILogger? _logger;

        public FormulaLoader(Config.IResourceLoader resourceLoader, ILogger? logger = null)
        {
            _resourceLoader = resourceLoader ?? throw new ArgumentNullException(nameof(resourceLoader));
            _logger = logger;
        }

        /// <summary>
        /// Carrega fórmulas da config ativa com herança delta.
        /// </summary>
        /// <param name="configChain">Cadeia de herança (base → mod)</param>
        /// <param name="strictMode">Se true, erros de delta causam exceções</param>
        /// <returns>Dicionário de fórmulas merged</returns>
        public Dictionary<string, FormulaDefinition> LoadFormulas(
            IEnumerable<string> configChain,
            bool strictMode = false)
        {
            // Carregar recursos JSON genéricos
            var rawData = _resourceLoader.LoadResource(
                "Pipelines/MathFormulas.json",
                configChain,
                strictMode
            );

            // Converter JsonElement → FormulaDefinition
            return ConvertToFormulas(rawData);
        }

        /// <summary>
        /// Converte dicionário de JsonElement para FormulaDefinition.
        /// </summary>
        private Dictionary<string, FormulaDefinition> ConvertToFormulas(
            Dictionary<string, JsonElement>? rawData)
        {
            var result = new Dictionary<string, FormulaDefinition>(StringComparer.OrdinalIgnoreCase);

            if (rawData == null)
            {
                _logger?.LogWarning("rawData is null, returning empty formula dictionary");
                return result;
            }

            _logger?.LogDebug($"Converting {rawData.Count} raw formulas to FormulaDefinition");
            
            foreach (var kvp in rawData)
            {
                _logger?.LogDebug($"Processing formula: {kvp.Key}");
                try
                {
                    var formula = JsonSerializer.Deserialize<FormulaDefinition>(
                        kvp.Value.GetRawText(),
                        new JsonSerializerOptions
                        {
                            PropertyNameCaseInsensitive = true
                        }
                    );

                    if (formula != null)
                    {
                        result[kvp.Key] = formula;
                        _logger?.LogDebug($"Successfully converted: {kvp.Key}");
                    }
                    else
                    {
                        _logger?.LogWarning($"Failed to deserialize formula '{kvp.Key}'");
                    }
                }
                catch (Exception ex)
                {
                    _logger?.LogError($"Error deserializing formula '{kvp.Key}': {ex.Message}", ex);
                }
            }
            
            _logger?.LogDebug($"Converted {result.Count} formulas successfully");

            return result;
        }

        /// <summary>
        /// Retorna de qual config cada fórmula veio (para introspecção).
        /// </summary>
        public Dictionary<string, string> GetFormulaOrigins()
        {
            return _resourceLoader.GetResourceOrigins("Pipelines/MathFormulas.json");
        }

        /// <summary>
        /// Invalida cache de fórmulas.
        /// </summary>
        public void InvalidateCache()
        {
            _resourceLoader.InvalidateCache("Pipelines/MathFormulas.json");
        }
    }
}
