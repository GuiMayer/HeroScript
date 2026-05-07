using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;

namespace Core.Math
{
    /// <summary>
    /// Modelo para representar uma fórmula completa do JSON
    /// </summary>
    public class FormulaDefinition
    {
        [JsonPropertyName("description")]
        public string Description { get; set; } = string.Empty;

        [JsonPropertyName("params")]
        public Dictionary<string, float> Params { get; set; } = new();

        [JsonPropertyName("operations")]
        public List<OperationDefinition> Operations { get; set; } = new();
    }

    /// <summary>
    /// Modelo para representar uma operação individual
    /// </summary>
    public class OperationDefinition
    {
        [JsonPropertyName("op")]
        public string Op { get; set; } = string.Empty;

        [JsonPropertyName("value")]
        public string? Value { get; set; }

        [JsonPropertyName("min")]
        public string? Min { get; set; }

        [JsonPropertyName("max")]
        public string? Max { get; set; }
    }

    /// <summary>
    /// Engine para construir MathExpression a partir de fórmulas definidas em JSON.
    /// Carrega fórmulas de Resources/Pipelines/MathFormulas.json e as converte
    /// em objetos MathExpression executáveis.
    /// Suporta herança delta: configs podem herdar fórmulas de configs pai.
    /// </summary>
    public class MathEngine
    {
        private static readonly object _cacheLock = new();
        
        // Cache estático de fórmulas carregadas
        private static Dictionary<string, FormulaDefinition>? _formulaCache;
        
        // Cache de origens das fórmulas (para introspecção)
        private static Dictionary<string, string>? _formulaOrigins;

        /// <summary>
        /// Invalida cache (chamado quando config muda)
        /// </summary>
        public static void ReloadFormulas()
        {
            lock (_cacheLock)
            {
                _formulaCache = null;
                _formulaOrigins = null;
                Console.WriteLine("[MathEngine] Cache invalidated");
            }
        }

        /// <summary>
        /// Retorna de qual config cada fórmula veio (para introspecção)
        /// </summary>
        public static Dictionary<string, string> GetFormulaOrigins()
        {
            return _formulaOrigins ?? new Dictionary<string, string>();
        }

        /// <summary>
        /// Carrega as fórmulas da config ativa (com herança delta)
        /// </summary>
        private Dictionary<string, FormulaDefinition> LoadFormulas()
        {
            if (_formulaCache != null)
                return _formulaCache;

            lock (_cacheLock)
            {
                if (_formulaCache != null)
                    return _formulaCache;

                // Usar FormulaLoader para carregar com herança delta
                var loader = new FormulaLoader();
                var chain = Config.ConfigManager.ResolveInheritanceChain(Config.ConfigManager.CurrentConfig);
                
                _formulaCache = loader.LoadFormulas(chain, strictMode: false);
                _formulaOrigins = loader.GetFormulaOrigins();
                
                return _formulaCache;
            }
        }

        /// <summary>
        /// Retorna lista de todas as fórmulas disponíveis no JSON
        /// </summary>
        public IEnumerable<string> GetAvailableFormulas()
        {
            return LoadFormulas().Keys;
        }

        /// <summary>
        /// Retorna descrição de uma fórmula específica
        /// </summary>
        public string? GetFormulaDescription(string formulaName)
        {
            var formulas = LoadFormulas();
            return formulas.TryGetValue(formulaName, out var formula) ? formula.Description : null;
        }

        /// <summary>
        /// Retorna parâmetros padrão de uma fórmula
        /// </summary>
        public Dictionary<string, float>? GetFormulaDefaultParams(string formulaName)
        {
            var formulas = LoadFormulas();
            return formulas.TryGetValue(formulaName, out var formula) ? formula.Params : null;
        }

        /// <summary>
        /// Constrói uma MathExpression a partir de uma fórmula do JSON
        /// </summary>
        /// <param name="formulaName">Nome da fórmula (ex: "HYPERBOLIC_CURVE")</param>
        /// <param name="inputValue">Valor inicial para a expressão</param>
        /// <param name="paramOverrides">Parâmetros customizados (opcional, sobrescreve defaults do JSON)</param>
        public MathExpression BuildFromFormula(
            string formulaName,
            float inputValue,
            Dictionary<string, float>? paramOverrides = null)
        {
            // 1. Carregar fórmulas do JSON
            var formulas = LoadFormulas();

            // 2. Validar que fórmula existe
            if (!formulas.TryGetValue(formulaName, out var formula))
                throw new ArgumentException($"Formula '{formulaName}' not found in MathFormulas.json");

            // 3. Mesclar parâmetros (defaults + overrides)
            var parameters = new Dictionary<string, float>(formula.Params, StringComparer.OrdinalIgnoreCase);
            if (paramOverrides != null)
            {
                foreach (var kvp in paramOverrides)
                    parameters[kvp.Key] = kvp.Value;
            }

            // 4. Validar parâmetros
            ValidateFormulaParameters(formula);

            // 5. Criar MathExpression com valor inicial
            var expression = new MathExpression(inputValue);

            // 6. Processar cada operação
            foreach (var operation in formula.Operations)
            {
                ApplyOperation(expression, operation, parameters);
            }

            return expression;
        }

        /// <summary>
        /// Valida que todos os parâmetros referenciados nas operações existem
        /// </summary>
        private void ValidateFormulaParameters(FormulaDefinition formula)
        {
            var availableParams = formula.Params.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (var operation in formula.Operations)
            {
                // Verificar campo 'value'
                if (operation.Value != null && operation.Value.StartsWith("params.", StringComparison.OrdinalIgnoreCase))
                {
                    string paramName = operation.Value.Substring(7);
                    if (!availableParams.Contains(paramName))
                        throw new InvalidOperationException(
                            $"Operation '{operation.Op}' references undefined parameter '{paramName}'");
                }

                // Verificar campos 'min' e 'max' (para CLAMP)
                if (operation.Min != null && operation.Min.StartsWith("params.", StringComparison.OrdinalIgnoreCase))
                {
                    string paramName = operation.Min.Substring(7);
                    if (!availableParams.Contains(paramName))
                        throw new InvalidOperationException(
                            $"CLAMP operation references undefined parameter '{paramName}' in min");
                }

                if (operation.Max != null && operation.Max.StartsWith("params.", StringComparison.OrdinalIgnoreCase))
                {
                    string paramName = operation.Max.Substring(7);
                    if (!availableParams.Contains(paramName))
                        throw new InvalidOperationException(
                            $"CLAMP operation references undefined parameter '{paramName}' in max");
                }
            }
        }

        /// <summary>
        /// Resolve um valor que pode ser literal (123.45) ou referência a parâmetro (params.NAME)
        /// </summary>
        private float ResolveValue(string valueStr, Dictionary<string, float> parameters)
        {
            if (string.IsNullOrWhiteSpace(valueStr))
                throw new ArgumentException("Value string cannot be null or empty");

            // Caso 1: Referência a parâmetro (params.PARAM_NAME)
            if (valueStr.StartsWith("params.", StringComparison.OrdinalIgnoreCase))
            {
                string paramName = valueStr.Substring(7); // Remove "params."

                if (!parameters.TryGetValue(paramName, out float paramValue))
                    throw new InvalidOperationException($"Parameter '{paramName}' not found in formula parameters");

                return paramValue;
            }

            // Caso 2: Literal numérico
            if (float.TryParse(valueStr, System.Globalization.NumberStyles.Float,
                               System.Globalization.CultureInfo.InvariantCulture, out float literalValue))
            {
                return literalValue;
            }

            // Caso 3: Valor inválido
            throw new ArgumentException($"Invalid value format: '{valueStr}'. Expected literal number or 'params.NAME'");
        }

        /// <summary>
        /// Aplica uma operação individual à MathExpression
        /// </summary>
        private void ApplyOperation(
            MathExpression expression,
            OperationDefinition operation,
            Dictionary<string, float> parameters)
        {
            string op = operation.Op.ToUpper();

            switch (op)
            {
                case "ADD":
                    ValidateOperationHasValue(operation, "ADD");
                    expression.Add(ResolveValue(operation.Value!, parameters));
                    break;

                case "SUBTRACT":
                    ValidateOperationHasValue(operation, "SUBTRACT");
                    expression.Subtract(ResolveValue(operation.Value!, parameters));
                    break;

                case "MULTIPLY":
                    ValidateOperationHasValue(operation, "MULTIPLY");
                    expression.Multiply(ResolveValue(operation.Value!, parameters));
                    break;

                case "DIVIDE":
                    ValidateOperationHasValue(operation, "DIVIDE");
                    expression.Divide(ResolveValue(operation.Value!, parameters));
                    break;

                case "DIVIDE_INVERSE":
                    ValidateOperationHasValue(operation, "DIVIDE_INVERSE");
                    expression.DivideInverse(ResolveValue(operation.Value!, parameters));
                    break;

                case "POW":
                    ValidateOperationHasValue(operation, "POW");
                    expression.Pow(ResolveValue(operation.Value!, parameters));
                    break;

                case "POW_BASE":
                    ValidateOperationHasValue(operation, "POW_BASE");
                    expression.PowBase(ResolveValue(operation.Value!, parameters));
                    break;

                case "SQRT":
                    expression.Sqrt();
                    break;

                case "LOG":
                    expression.Log();
                    break;

                case "NEGATE":
                    expression.Negate();
                    break;

                case "CLAMP":
                    ValidateOperationHasMinMax(operation);
                    float min = ResolveValue(operation.Min!, parameters);
                    float max = ResolveValue(operation.Max!, parameters);
                    expression.Clamp(min, max);
                    break;

                default:
                    throw new InvalidOperationException($"Unknown operation: '{operation.Op}'");
            }
        }

        /// <summary>
        /// Valida que operação tem campo 'value' preenchido
        /// </summary>
        private void ValidateOperationHasValue(OperationDefinition operation, string opName)
        {
            if (string.IsNullOrWhiteSpace(operation.Value))
                throw new InvalidOperationException($"Operation '{opName}' requires a 'value' field");
        }

        /// <summary>
        /// Valida que operação CLAMP tem campos 'min' e 'max' preenchidos
        /// </summary>
        private void ValidateOperationHasMinMax(OperationDefinition operation)
        {
            if (string.IsNullOrWhiteSpace(operation.Min))
                throw new InvalidOperationException("CLAMP operation requires a 'min' field");
            if (string.IsNullOrWhiteSpace(operation.Max))
                throw new InvalidOperationException("CLAMP operation requires a 'max' field");
        }
    }
}
