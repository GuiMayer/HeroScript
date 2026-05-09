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
    /// 
    /// SISTEMA HÍBRIDO: ACUMULADOR IMPLÍCITO + OPERANDOS EXPLÍCITOS
    /// 
    /// MODO 1: Acumulador Implícito (padrão, retrocompatível)
    /// - Use 'value' para operações que modificam o acumulador (currentValue)
    /// - Exemplo: { "op": "ADD", "value": "params.BONUS" }
    /// - Comportamento: currentValue = currentValue + BONUS
    /// 
    /// MODO 2: Operandos Explícitos (novo, para expressões complexas)
    /// - Use 'operands' para especificar todos os operandos explicitamente
    /// - Exemplo: { "op": "SUBTRACT", "operands": ["params.TARGET", "params.START"] }
    /// - Comportamento: currentValue = TARGET - START (ignora currentValue anterior)
    /// 
    /// Referências especiais em operandos:
    /// - $current: valor do acumulador atual
    /// - $initial: valor inicial da expressão
    /// - params.NAME: parâmetro da fórmula
    /// - 123.45: literal numérico
    /// 
    /// IMPORTANTE: Uma operação NÃO PODE ter 'value' E 'operands' ao mesmo tempo.
    /// São dois modos excludentes para definir uma fórmula.
    /// </summary>
    public class OperationDefinition
    {
        [JsonPropertyName("op")]
        public string Op { get; set; } = string.Empty;

        [JsonPropertyName("value")]
        public string? Value { get; set; }

        [JsonPropertyName("operands")]
        public List<string>? Operands { get; set; }

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
    public class MathEngine : IMathEngine
    {
        private readonly object _cacheLock = new();
        private readonly Config.IConfigManager _configManager;
        private readonly FormulaLoader _formulaLoader;
        private readonly Events.IEventBus? _eventBus;
        private readonly Logging.ILogger _logger;
        
        // Cache de fórmulas carregadas
        private Dictionary<string, FormulaDefinition>? _formulaCache;
        
        // Cache de origens das fórmulas (para introspecção)
        private Dictionary<string, string>? _formulaOrigins;

        /// <summary>
        /// Constructor for dependency injection
        /// </summary>
        public MathEngine(Config.IConfigManager configManager, FormulaLoader formulaLoader, Logging.ILogger logger, Events.IEventBus? eventBus = null)
        {
            _configManager = configManager ?? throw new ArgumentNullException(nameof(configManager));
            _formulaLoader = formulaLoader ?? throw new ArgumentNullException(nameof(formulaLoader));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _eventBus = eventBus;
        }

        /// <summary>
        /// Retorna de qual config cada fórmula veio (para introspecção)
        /// </summary>
        public Dictionary<string, string> GetFormulaOrigins()
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

                _logger.LogDebug($"Loading formulas for config '{_configManager.CurrentConfig}'");

                // Usar FormulaLoader para carregar com herança delta
                var chain = _configManager.ResolveInheritanceChain(_configManager.CurrentConfig);
                
                _formulaCache = _formulaLoader.LoadFormulas(chain, strictMode: false);
                _formulaOrigins = _formulaLoader.GetFormulaOrigins();
                
                _logger.LogInformation($"Loaded {_formulaCache.Count} formulas from config chain");
                
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
        /// Checks if a formula exists.
        /// </summary>
        public bool FormulaExists(string formulaName)
        {
            return LoadFormulas().ContainsKey(formulaName);
        }

        /// <summary>
        /// Invalidates the formula cache, forcing reload on next access.
        /// </summary>
        public void InvalidateCache()
        {
            lock (_cacheLock)
            {
                _formulaCache = null;
                _formulaOrigins = null;
                _logger.LogDebug("Formula cache invalidated");
            }
        }

        /// <summary>
        /// Gets cache statistics for monitoring and diagnostics.
        /// </summary>
        public Dictionary<string, object> GetCacheStats()
        {
            lock (_cacheLock)
            {
                var stats = new Dictionary<string, object>
                {
                    ["IsCached"] = _formulaCache != null,
                    ["FormulaCount"] = _formulaCache?.Count ?? 0
                };

                if (_formulaCache != null)
                {
                    stats["Formulas"] = _formulaCache.Keys.ToList();
                }

                if (_formulaOrigins != null)
                {
                    stats["Origins"] = _formulaOrigins;
                }

                return stats;
            }
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
        /// Retorna parâmetros mesclados (defaults + overrides) de uma fórmula
        /// </summary>
        /// <param name="formulaName">Nome da fórmula</param>
        /// <param name="paramOverrides">Parâmetros customizados (opcional, sobrescreve defaults do JSON)</param>
        /// <returns>Result contendo dicionário com parâmetros mesclados, ou falha se fórmula não existir</returns>
        public Common.Result<Dictionary<string, float>> GetMergedParams(string formulaName, Dictionary<string, float>? paramOverrides = null)
        {
            if (string.IsNullOrWhiteSpace(formulaName))
            {
                _logger.LogWarning("GetMergedParams called with empty formula name");
                return Common.Result<Dictionary<string, float>>.Failure("Formula name cannot be empty");
            }

            var formulas = LoadFormulas();
            if (!formulas.TryGetValue(formulaName, out var formula))
            {
                _logger.LogWarning($"Formula '{formulaName}' not found in GetMergedParams");
                return Common.Result<Dictionary<string, float>>.Failure($"Formula '{formulaName}' not found");
            }

            var parameters = new Dictionary<string, float>(formula.Params, StringComparer.OrdinalIgnoreCase);
            if (paramOverrides != null)
            {
                foreach (var kvp in paramOverrides)
                    parameters[kvp.Key] = kvp.Value;
            }

            _logger.LogDebug($"Merged parameters for formula '{formulaName}': {parameters.Count} params");
            return Common.Result<Dictionary<string, float>>.Success(parameters);
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
            _logger.LogDebug($"Building formula '{formulaName}' with input={inputValue}, overrides={paramOverrides?.Count ?? 0}");

            // 1. Validar inputs básicos (Fase 1)
            ValidateFormulaName(formulaName);
            ValidateInputValue(inputValue, nameof(inputValue));
            ValidateParameterOverrides(paramOverrides);

            // 2. Carregar fórmulas do JSON
            var formulas = LoadFormulas();

            // 3. Validar que fórmula existe
            if (!formulas.TryGetValue(formulaName, out var formula))
            {
                _logger.LogError($"Formula '{formulaName}' not found in MathFormulas.json");
                throw new ArgumentException($"Formula '{formulaName}' not found in MathFormulas.json");
            }

            // 4. Mesclar parâmetros (defaults + overrides)
            var parameters = new Dictionary<string, float>(formula.Params, StringComparer.OrdinalIgnoreCase);
            if (paramOverrides != null)
            {
                foreach (var kvp in paramOverrides)
                    parameters[kvp.Key] = kvp.Value;
            }

            // 5. Validar parâmetros da fórmula
            ValidateFormulaParameters(formula);

            // 6. Validar input contextual (Fase 2)
            ValidateInputForFormula(inputValue, formula, formulaName);

            // 7. Validar uso de parâmetros (Fase 2)
            ValidateParameterUsage(formula, parameters, formulaName);

            // 8. Criar MathExpression com valor inicial
            var expression = new MathExpression(inputValue);

            // 9. Processar cada operação com rastreamento completo de currentValue
            // Para operandos explícitos que usam $current, precisamos rastrear o valor atual
            float currentValue = inputValue;
            
            foreach (var operation in formula.Operations)
            {
                ApplyOperation(expression, operation, parameters, inputValue, currentValue);
                
                // Atualizar currentValue após cada operação
                // Simular a execução para obter o novo currentValue
                currentValue = SimulateCurrentValue(expression);
            }

            // 10. Calcular resultado final
            var result = expression.Build();

            _logger.LogDebug($"Formula '{formulaName}' evaluated: {inputValue} -> {result}");

            // 11. Publicar evento se EventBus estiver configurado
            _eventBus?.Publish(new Events.Domain.MathFormulaEvaluatedEvent
            {
                FormulaName = formulaName,
                InputValue = inputValue,
                OutputValue = result,
                Parameters = parameters,
                Target = formulaName
            });

            return expression;
        }

        /// <summary>
        /// Simula a execução da MathExpression até o momento atual para obter o currentValue.
        /// Necessário para rastrear o valor do acumulador durante a construção quando
        /// operandos explícitos usam $current.
        /// </summary>
        private float SimulateCurrentValue(MathExpression expression)
        {
            // Executar Build() para obter o resultado atual
            // Nota: Build() é idempotente e pode ser chamado múltiplas vezes
            return expression.Build();
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

                // Verificar operandos explícitos
                if (operation.Operands != null)
                {
                    foreach (var operand in operation.Operands)
                    {
                        if (operand.StartsWith("params.", StringComparison.OrdinalIgnoreCase))
                        {
                            string paramName = operand.Substring(7);
                            if (!availableParams.Contains(paramName))
                                throw new InvalidOperationException(
                                    $"Operation '{operation.Op}' references undefined parameter '{paramName}' in operands");
                        }
                        else if (!operand.StartsWith("$", StringComparison.OrdinalIgnoreCase))
                        {
                            // Validar que é um literal numérico válido
                            if (!float.TryParse(operand, System.Globalization.NumberStyles.Float,
                                               System.Globalization.CultureInfo.InvariantCulture, out _))
                            {
                                throw new InvalidOperationException(
                                    $"Invalid operand '{operand}' in operation '{operation.Op}'. " +
                                    "Expected: $current, $initial, params.NAME, or numeric literal");
                            }
                        }
                    }
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
            // Reutilizar método estático para evitar duplicação
            return ResolveValueStatic(valueStr, parameters);
        }

        /// <summary>
        /// Resolve um operando que pode ser:
        /// - $current: valor do acumulador atual
        /// - $initial: valor inicial da expressão
        /// - params.NAME: parâmetro da fórmula
        /// - 123.45: literal numérico
        /// </summary>
        private float ResolveOperand(
            string operandStr,
            Dictionary<string, float> parameters,
            float initialValue,
            float currentValue)
        {
            if (string.IsNullOrWhiteSpace(operandStr))
                throw new ArgumentException("Operand string cannot be null or empty");

            // Referências especiais
            if (operandStr.Equals("$current", StringComparison.OrdinalIgnoreCase))
                return currentValue;

            if (operandStr.Equals("$initial", StringComparison.OrdinalIgnoreCase))
                return initialValue;

            // Reutilizar ResolveValue() para params.X e literais
            return ResolveValue(operandStr, parameters);
        }

        /// <summary>
        /// Avalia uma operação com operandos explícitos e retorna o resultado.
        /// Não modifica o acumulador - apenas calcula o resultado.
        /// Suporta todas as operações existentes do MathExpression.
        /// </summary>
        private float EvaluateExplicitOperation(string op, float[] operands)
        {
            switch (op)
            {
                case "ADD":
                    if (operands.Length < 2)
                        throw new ArgumentException($"ADD with explicit operands requires at least 2 operands, got {operands.Length}");
                    float sum = operands[0];
                    for (int i = 1; i < operands.Length; i++)
                        sum += operands[i];
                    return sum;

                case "SUBTRACT":
                    if (operands.Length != 2)
                        throw new ArgumentException($"SUBTRACT with explicit operands requires exactly 2 operands, got {operands.Length}");
                    return operands[0] - operands[1];

                case "MULTIPLY":
                    if (operands.Length < 2)
                        throw new ArgumentException($"MULTIPLY with explicit operands requires at least 2 operands, got {operands.Length}");
                    float product = operands[0];
                    for (int i = 1; i < operands.Length; i++)
                        product *= operands[i];
                    return product;

                case "DIVIDE":
                    if (operands.Length != 2)
                        throw new ArgumentException($"DIVIDE with explicit operands requires exactly 2 operands, got {operands.Length}");
                    if (operands[1] == 0)
                        throw new DivideByZeroException("Cannot divide by zero");
                    return operands[0] / operands[1];

                case "DIVIDE_INVERSE":
                    if (operands.Length != 2)
                        throw new ArgumentException($"DIVIDE_INVERSE with explicit operands requires exactly 2 operands (numerator, denominator), got {operands.Length}");
                    if (operands[1] == 0)
                        throw new DivideByZeroException("Cannot divide by zero (denominator is zero in inverse division)");
                    return operands[0] / operands[1];

                case "POW":
                    if (operands.Length != 2)
                        throw new ArgumentException($"POW with explicit operands requires exactly 2 operands (base, exponent), got {operands.Length}");
                    return (float)System.Math.Pow(operands[0], operands[1]);

                case "POW_BASE":
                    if (operands.Length != 2)
                        throw new ArgumentException($"POW_BASE with explicit operands requires exactly 2 operands (base, exponent), got {operands.Length}");
                    return (float)System.Math.Pow(operands[0], operands[1]);

                case "SQRT":
                    if (operands.Length != 1)
                        throw new ArgumentException($"SQRT with explicit operands requires exactly 1 operand, got {operands.Length}");
                    if (operands[0] < 0)
                        throw new InvalidOperationException("Cannot take the square root of a negative number");
                    return (float)System.Math.Sqrt(operands[0]);

                case "LOG":
                    if (operands.Length == 1)
                    {
                        // Logaritmo natural
                        if (operands[0] <= 0)
                            throw new InvalidOperationException("Cannot take the logarithm of a non-positive number");
                        return (float)System.Math.Log(operands[0]);
                    }
                    else if (operands.Length == 2)
                    {
                        // Logaritmo com base customizada: log_b(x)
                        if (operands[0] <= 0)
                            throw new InvalidOperationException("Cannot take the logarithm of a non-positive number");
                        if (operands[1] <= 0 || operands[1] == 1)
                            throw new InvalidOperationException("Logarithm base must be positive and not equal to 1");
                        
                        if (operands[1] == 10f)
                            return (float)System.Math.Log10(operands[0]);
                        else
                            return (float)(System.Math.Log(operands[0]) / System.Math.Log(operands[1]));
                    }
                    else
                    {
                        throw new ArgumentException($"LOG with explicit operands requires 1 operand (value) or 2 operands (value, base), got {operands.Length}");
                    }

                case "NEGATE":
                    if (operands.Length != 1)
                        throw new ArgumentException($"NEGATE with explicit operands requires exactly 1 operand, got {operands.Length}");
                    return -operands[0];

                case "CLAMP":
                    if (operands.Length != 3)
                        throw new ArgumentException($"CLAMP with explicit operands requires exactly 3 operands (value, min, max), got {operands.Length}");
                    if (operands[1] > operands[2])
                        throw new ArgumentException($"CLAMP min ({operands[1]}) cannot be greater than max ({operands[2]})");
                    return System.Math.Max(operands[1], System.Math.Min(operands[2], operands[0]));

                case "MIN":
                    if (operands.Length < 2)
                        throw new ArgumentException($"MIN with explicit operands requires at least 2 operands, got {operands.Length}");
                    float min = operands[0];
                    for (int i = 1; i < operands.Length; i++)
                        min = System.Math.Min(min, operands[i]);
                    return min;

                case "MAX":
                    if (operands.Length < 2)
                        throw new ArgumentException($"MAX with explicit operands requires at least 2 operands, got {operands.Length}");
                    float max = operands[0];
                    for (int i = 1; i < operands.Length; i++)
                        max = System.Math.Max(max, operands[i]);
                    return max;

                case "ABS":
                    if (operands.Length != 1)
                        throw new ArgumentException($"ABS with explicit operands requires exactly 1 operand, got {operands.Length}");
                    return System.Math.Abs(operands[0]);

                case "ROUND":
                    if (operands.Length == 1)
                        return (float)System.Math.Round(operands[0]);
                    else if (operands.Length == 2)
                        return (float)System.Math.Round(operands[0], (int)operands[1]);
                    else
                        throw new ArgumentException($"ROUND with explicit operands requires 1 operand (value) or 2 operands (value, decimals), got {operands.Length}");

                case "FLOOR":
                    if (operands.Length != 1)
                        throw new ArgumentException($"FLOOR with explicit operands requires exactly 1 operand, got {operands.Length}");
                    return (float)System.Math.Floor(operands[0]);

                case "CEIL":
                    if (operands.Length != 1)
                        throw new ArgumentException($"CEIL with explicit operands requires exactly 1 operand, got {operands.Length}");
                    return (float)System.Math.Ceiling(operands[0]);

                case "MODULO":
                case "MOD":
                    if (operands.Length != 2)
                        throw new ArgumentException($"MODULO with explicit operands requires exactly 2 operands (dividend, divisor), got {operands.Length}");
                    if (operands[1] == 0)
                        throw new DivideByZeroException("Cannot compute modulo with divisor zero");
                    return operands[0] % operands[1];

                default:
                    throw new NotSupportedException($"Operation '{op}' does not support explicit operands yet");
            }
        }

        /// <summary>
        /// Aplica uma operação individual à MathExpression.
        /// Suporta dois modos:
        /// - Modo implícito: usa 'value' e modifica o acumulador (retrocompatível)
        /// - Modo explícito: usa 'operands' e calcula resultado independente
        /// </summary>
        private void ApplyOperation(
            MathExpression expression,
            OperationDefinition operation,
            Dictionary<string, float> parameters,
            float initialValue,
            float currentValue)
        {
            string op = operation.Op.ToUpper();

            // VALIDAÇÃO: Não pode ter 'value' E 'operands' ao mesmo tempo
            if (operation.Value != null && operation.Operands != null && operation.Operands.Count > 0)
            {
                throw new InvalidOperationException(
                    $"Operation '{op}' cannot have both 'value' and 'operands'. " +
                    "These are two mutually exclusive modes for defining a formula. " +
                    "Use 'value' for implicit accumulator mode, or 'operands' for explicit operands mode.");
            }

            // MODO 1: Operandos explícitos (novo)
            if (operation.Operands != null && operation.Operands.Count > 0)
            {
                // Resolver todos os operandos
                var resolvedOperands = operation.Operands
                    .Select(o => ResolveOperand(o, parameters, initialValue, currentValue))
                    .ToArray();

                // Avaliar operação e definir resultado no acumulador
                float result = EvaluateExplicitOperation(op, resolvedOperands);
                expression.Set(result);
                return;
            }

            // MODO 2: Acumulador implícito (existente, 100% retrocompatível)
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

                case "MIN":
                    ValidateOperationHasValue(operation, "MIN");
                    expression.Min(ResolveValue(operation.Value!, parameters));
                    break;

                case "MAX":
                    ValidateOperationHasValue(operation, "MAX");
                    expression.Max(ResolveValue(operation.Value!, parameters));
                    break;

                case "ABS":
                    expression.Abs();
                    break;

                case "SET":
                    ValidateOperationHasValue(operation, "SET");
                    expression.Set(ResolveValue(operation.Value!, parameters));
                    break;

                case "ROUND":
                    int decimals = operation.Value != null 
                        ? (int)ResolveValue(operation.Value, parameters) 
                        : 0;
                    expression.Round(decimals);
                    break;

                case "FLOOR":
                    expression.Floor();
                    break;

                case "CEIL":
                    expression.Ceil();
                    break;

                case "MODULO":
                case "MOD":
                    ValidateOperationHasValue(operation, "MODULO");
                    expression.Modulo(ResolveValue(operation.Value!, parameters));
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

        // ========================================
        // FASE 1: VALIDAÇÕES BÁSICAS DE INPUT
        // ========================================

        /// <summary>
        /// Valida que o nome da fórmula não é null ou vazio
        /// </summary>
        private void ValidateFormulaName(string formulaName)
        {
            if (string.IsNullOrWhiteSpace(formulaName))
                throw new ArgumentException("Formula name cannot be null or empty.", nameof(formulaName));
        }

        /// <summary>
        /// Valida que o valor de input não é NaN ou Infinity
        /// </summary>
        private void ValidateInputValue(float value, string paramName)
        {
            if (float.IsNaN(value))
                throw new ArgumentException($"{paramName} cannot be NaN (Not a Number).", paramName);
            
            if (float.IsInfinity(value))
                throw new ArgumentException($"{paramName} cannot be Infinity.", paramName);
        }

        /// <summary>
        /// Valida que os parâmetros customizados não contêm NaN ou Infinity
        /// </summary>
        private void ValidateParameterOverrides(Dictionary<string, float>? paramOverrides)
        {
            if (paramOverrides == null)
                return;

            foreach (var kvp in paramOverrides)
            {
                if (float.IsNaN(kvp.Value))
                    throw new ArgumentException(
                        $"Parameter override '{kvp.Key}' cannot be NaN (Not a Number).", 
                        nameof(paramOverrides));
                
                if (float.IsInfinity(kvp.Value))
                    throw new ArgumentException(
                        $"Parameter override '{kvp.Key}' cannot be Infinity.", 
                        nameof(paramOverrides));
            }
        }

        // ========================================
        // FASE 2: VALIDAÇÕES CONTEXTUAIS
        // ========================================

        /// <summary>
        /// Valida que o input é compatível com a primeira operação da fórmula
        /// </summary>
        private void ValidateInputForFormula(float inputValue, FormulaDefinition formula, string formulaName)
        {
            if (formula.Operations.Count == 0)
                return;

            var firstOp = formula.Operations[0].Op.ToUpper();

            switch (firstOp)
            {
                case "SQRT":
                    if (inputValue < 0)
                        throw new ArgumentException(
                            $"Formula '{formulaName}' starts with SQRT operation, but input value is negative ({inputValue}). " +
                            $"SQRT requires non-negative input.",
                            nameof(inputValue));
                    break;

                case "LOG":
                    if (inputValue <= 0)
                        throw new ArgumentException(
                            $"Formula '{formulaName}' starts with LOG operation, but input value is non-positive ({inputValue}). " +
                            $"LOG requires positive input.",
                            nameof(inputValue));
                    break;

                case "DIVIDE_INVERSE":
                    if (inputValue == 0)
                        throw new ArgumentException(
                            $"Formula '{formulaName}' starts with DIVIDE_INVERSE operation, but input value is zero. " +
                            $"DIVIDE_INVERSE requires non-zero input (would cause division by zero).",
                            nameof(inputValue));
                    break;
            }
        }

        /// <summary>
        /// Valida que os parâmetros são compatíveis com o uso nas operações
        /// </summary>
        private void ValidateParameterUsage(FormulaDefinition formula, Dictionary<string, float> parameters, string formulaName)
        {
            foreach (var operation in formula.Operations)
            {
                string op = operation.Op.ToUpper();

                // Validar parâmetros usados em divisão
                if (op == "DIVIDE" && operation.Value != null)
                {
                    if (operation.Value.StartsWith("params.", StringComparison.OrdinalIgnoreCase))
                    {
                        string paramName = operation.Value.Substring(7);
                        if (parameters.TryGetValue(paramName, out float paramValue) && paramValue == 0)
                        {
                            throw new InvalidOperationException(
                                $"Formula '{formulaName}' uses parameter '{paramName}' in DIVIDE operation, " +
                                $"but parameter value is zero (would cause division by zero).");
                        }
                    }
                }

                // Validar parâmetros usados em DIVIDE_INVERSE
                if (op == "DIVIDE_INVERSE" && operation.Value != null)
                {
                    if (operation.Value.StartsWith("params.", StringComparison.OrdinalIgnoreCase))
                    {
                        string paramName = operation.Value.Substring(7);
                        if (parameters.TryGetValue(paramName, out float paramValue) && paramValue == 0)
                        {
                            throw new InvalidOperationException(
                                $"Formula '{formulaName}' uses parameter '{paramName}' in DIVIDE_INVERSE operation, " +
                                $"but parameter value is zero (would cause division by zero).");
                        }
                    }
                }

                // Validar parâmetros usados em LOG base
                if (op == "LOG" && operation.Value != null)
                {
                    if (operation.Value.StartsWith("params.", StringComparison.OrdinalIgnoreCase))
                    {
                        string paramName = operation.Value.Substring(7);
                        if (parameters.TryGetValue(paramName, out float paramValue))
                        {
                            if (paramValue <= 0)
                            {
                                throw new InvalidOperationException(
                                    $"Formula '{formulaName}' uses parameter '{paramName}' as LOG base, " +
                                    $"but parameter value is non-positive ({paramValue}). LOG base must be positive.");
                            }
                            if (paramValue == 1)
                            {
                                throw new InvalidOperationException(
                                    $"Formula '{formulaName}' uses parameter '{paramName}' as LOG base, " +
                                    $"but parameter value is 1. LOG base cannot be 1.");
                            }
                        }
                    }
                }

                // Validar parâmetros usados em POW_BASE
                if (op == "POW_BASE" && operation.Value != null)
                {
                    if (operation.Value.StartsWith("params.", StringComparison.OrdinalIgnoreCase))
                    {
                        string paramName = operation.Value.Substring(7);
                        if (parameters.TryGetValue(paramName, out float paramValue))
                        {
                            // Validar casos problemáticos: base negativa com expoente fracionário
                            // Nota: Esta validação é conservadora - permite base negativa com expoentes inteiros
                            if (paramValue < 0)
                            {
                                throw new InvalidOperationException(
                                    $"Formula '{formulaName}' uses parameter '{paramName}' as POW_BASE base, " +
                                    $"but parameter value is negative ({paramValue}). " +
                                    $"Negative bases can cause issues with non-integer exponents.");
                            }
                        }
                    }
                }
            }
        }

        // ============================================================================
        // MÉTODOS PÚBLICOS PARA API
        // ============================================================================

        /// <summary>
        /// Resolve um operando string para um valor float.
        /// Suporta: "$current", "$initial", "params.NAME", e literais numéricos.
        /// Método público para uso pela API.
        /// </summary>
        public static float ResolveOperandPublic(
            string operandStr,
            Dictionary<string, float> parameters,
            float initialValue,
            float currentValue)
        {
            if (string.IsNullOrWhiteSpace(operandStr))
                throw new ArgumentException("Operand string cannot be null or empty");

            // Referências especiais
            if (operandStr.Equals("$current", StringComparison.OrdinalIgnoreCase))
                return currentValue;

            if (operandStr.Equals("$initial", StringComparison.OrdinalIgnoreCase))
                return initialValue;

            // Reutilizar lógica comum para params.X e literais
            return ResolveValueStatic(operandStr, parameters);
        }

        /// <summary>
        /// Método auxiliar estático para resolver valores (params.X ou literais).
        /// Extraído para evitar duplicação entre ResolveValue e ResolveOperandPublic.
        /// </summary>
        private static float ResolveValueStatic(string valueStr, Dictionary<string, float> parameters)
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
        /// Simula a execução de uma operação e retorna o novo valor do acumulador.
        /// Usado para rastrear $current durante a construção de expressões com operandos explícitos.
        /// </summary>
        public static float SimulateOperationResult(string operation, float currentValue, float[] operands)
        {
            switch (operation.ToUpper())
            {
                case "ADD":
                    return operands.Aggregate(currentValue, (acc, v) => acc + v);
                
                case "SUBTRACT":
                    if (operands.Length == 0)
                        throw new ArgumentException("SUBTRACT requires at least one operand");
                    return operands.Aggregate(currentValue, (acc, v) => acc - v);
                
                case "MULTIPLY":
                    return operands.Aggregate(currentValue, (acc, v) => acc * v);
                
                case "DIVIDE":
                    if (operands.Length == 0)
                        throw new ArgumentException("DIVIDE requires at least one operand");
                    foreach (var v in operands)
                    {
                        if (v == 0)
                            throw new DivideByZeroException("Cannot divide by zero");
                        currentValue /= v;
                    }
                    return currentValue;
                
                case "POW":
                    if (operands.Length != 1)
                        throw new ArgumentException("POW requires exactly one operand (exponent)");
                    return (float)System.Math.Pow(currentValue, operands[0]);
                
                case "SQRT":
                    if (currentValue < 0)
                        throw new ArgumentException("Cannot take square root of negative number");
                    return (float)System.Math.Sqrt(currentValue);
                
                case "ABS":
                    return System.Math.Abs(currentValue);
                
                case "ROUND":
                    int decimals = operands.Length > 0 ? (int)operands[0] : 0;
                    return (float)System.Math.Round(currentValue, decimals);
                
                case "FLOOR":
                    return (float)System.Math.Floor(currentValue);
                
                case "CEIL":
                    return (float)System.Math.Ceiling(currentValue);
                
                case "MIN":
                    if (operands.Length == 0)
                        return currentValue;
                    return System.Math.Min(currentValue, operands.Min());
                
                case "MAX":
                    if (operands.Length == 0)
                        return currentValue;
                    return System.Math.Max(currentValue, operands.Max());
                
                case "CLAMP":
                    if (operands.Length != 2)
                        throw new ArgumentException("CLAMP requires exactly two operands (min, max)");
                    return System.Math.Clamp(currentValue, operands[0], operands[1]);
                
                case "SET":
                    if (operands.Length != 1)
                        throw new ArgumentException("SET requires exactly one operand");
                    return operands[0];
                
                case "NEGATE":
                    return -currentValue;
                
                default:
                    throw new InvalidOperationException($"Unknown operation: {operation}");
            }
        }

        /// <summary>
        /// Valida que todos os operandos simbólicos podem ser resolvidos.
        /// Retorna lista de erros encontrados (vazia se tudo OK).
        /// </summary>
        public static List<string> ValidateOperands(
            List<string> operands,
            Dictionary<string, float>? parameters)
        {
            var errors = new List<string>();
            
            foreach (var operand in operands)
            {
                if (string.IsNullOrWhiteSpace(operand))
                {
                    errors.Add("Operand cannot be null or empty");
                    continue;
                }

                // Referências especiais são sempre válidas
                if (operand.Equals("$current", StringComparison.OrdinalIgnoreCase) ||
                    operand.Equals("$initial", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                // Parâmetros: verificar se existe
                if (operand.StartsWith("params.", StringComparison.OrdinalIgnoreCase))
                {
                    if (parameters == null)
                    {
                        errors.Add($"Operand '{operand}' requires parameters dictionary, but none was provided");
                        continue;
                    }

                    string paramName = operand.Substring(7);
                    if (!parameters.ContainsKey(paramName))
                    {
                        errors.Add($"Parameter '{paramName}' not found in parameters dictionary");
                    }
                    continue;
                }

                // Literal numérico: verificar se é válido
                if (!float.TryParse(operand, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out _))
                {
                    errors.Add($"Invalid operand: '{operand}'. Must be $current, $initial, params.NAME, or a numeric literal");
                }
            }

            return errors;
        }
    }
}
