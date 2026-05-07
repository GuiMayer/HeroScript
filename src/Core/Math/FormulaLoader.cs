using System;
using System.Collections.Generic;
using System.Text.Json;

namespace Core.Math
{
    /// <summary>
    /// Loader específico para fórmulas matemáticas.
    /// Usa o ResourceLoader genérico e converte JsonElement para FormulaDefinition.
    /// </summary>
    public class FormulaLoader
    {
        private readonly Config.ResourceLoader _resourceLoader;

        public FormulaLoader()
        {
            _resourceLoader = Config.ResourceLoader.Instance;
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
            Dictionary<string, JsonElement> rawData)
        {
            var result = new Dictionary<string, FormulaDefinition>(StringComparer.OrdinalIgnoreCase);

            foreach (var kvp in rawData)
            {
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
                    }
                    else
                    {
                        Console.WriteLine($"[FormulaLoader] Warning: Failed to deserialize formula '{kvp.Key}'");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[FormulaLoader] Error deserializing formula '{kvp.Key}': {ex.Message}");
                }
            }

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
