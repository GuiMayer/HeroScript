namespace Core.Math;

/// <summary>
/// Provedor de metadados de operações matemáticas
/// Fonte única de verdade para informações sobre operações disponíveis
/// </summary>
public class OperationMetadataProvider : IOperationMetadataProvider
{
    private readonly List<OperationMetadata> _operations;

    public OperationMetadataProvider()
    {
        _operations = InitializeOperations();
    }

    /// <summary>
    /// Obtém todas as operações disponíveis
    /// </summary>
    public IReadOnlyList<OperationMetadata> GetAllOperations()
    {
        return _operations.AsReadOnly();
    }

    /// <summary>
    /// Obtém metadados de uma operação específica
    /// </summary>
    public OperationMetadata? GetOperation(string name)
    {
        return _operations.FirstOrDefault(op =>
            op.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Obtém operações agrupadas por categoria
    /// </summary>
    public IReadOnlyDictionary<string, IReadOnlyList<OperationMetadata>> GetOperationsByCategory()
    {
        return _operations
            .GroupBy(op => op.Category)
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlyList<OperationMetadata>)g.ToList().AsReadOnly()
            );
    }

    /// <summary>
    /// Inicializa a lista de operações disponíveis
    /// Esta é a fonte única de verdade para metadados de operações
    /// </summary>
    private static List<OperationMetadata> InitializeOperations()
    {
        return new List<OperationMetadata>
        {
            // Basic arithmetic operations
            new OperationMetadata
            {
                Name = "ADD",
                Symbol = "+",
                Description = "Add values to the current result (accumulator mode)",
                MinValues = 1,
                MaxValues = -1,
                Category = "basic",
                Behavior = "accumulator",
                IsUnary = false
            },
            new OperationMetadata
            {
                Name = "SUBTRACT",
                Symbol = "-",
                Description = "Subtract values from the current result (accumulator mode)",
                MinValues = 1,
                MaxValues = -1,
                Category = "basic",
                Behavior = "accumulator",
                IsUnary = false
            },
            new OperationMetadata
            {
                Name = "MULTIPLY",
                Symbol = "*",
                Description = "Multiply the current result by values (accumulator mode)",
                MinValues = 1,
                MaxValues = -1,
                Category = "basic",
                Behavior = "accumulator",
                IsUnary = false
            },
            new OperationMetadata
            {
                Name = "DIVIDE",
                Symbol = "/",
                Description = "Divide the current result by values (accumulator mode)",
                MinValues = 1,
                MaxValues = -1,
                Category = "basic",
                Behavior = "accumulator",
                IsUnary = false
            },
            new OperationMetadata
            {
                Name = "DIVIDE_INVERSE",
                Symbol = "÷⁻¹",
                Description = "Divide a numerator by the current result (inverse division)",
                MinValues = 1,
                MaxValues = 1,
                Category = "advanced",
                Behavior = "unary",
                IsUnary = true
            },

            // Power operations
            new OperationMetadata
            {
                Name = "POW",
                Symbol = "^",
                Description = "Raise the current result to a power (accumulator mode)",
                MinValues = 1,
                MaxValues = -1,
                Category = "advanced",
                Behavior = "accumulator",
                IsUnary = false
            },
            new OperationMetadata
            {
                Name = "POW_BASE",
                Symbol = "base^x",
                Description = "Raise a base to the power of the current result",
                MinValues = 1,
                MaxValues = 1,
                Category = "advanced",
                Behavior = "unary",
                IsUnary = true
            },
            new OperationMetadata
            {
                Name = "SQRT",
                Symbol = "√",
                Description = "Calculate the square root of the current result",
                MinValues = 0,
                MaxValues = 0,
                Category = "advanced",
                Behavior = "unary",
                IsUnary = true
            },

            // Logarithm
            new OperationMetadata
            {
                Name = "LOG",
                Symbol = "log",
                Description = "Calculate logarithm of the current result (natural log if no base provided)",
                MinValues = 0,
                MaxValues = 1,
                Category = "advanced",
                Behavior = "unary-optional",
                IsUnary = true
            },

            // Unary operations
            new OperationMetadata
            {
                Name = "NEGATE",
                Symbol = "-x",
                Description = "Negate the current result (multiply by -1)",
                MinValues = 0,
                MaxValues = 0,
                Category = "basic",
                Behavior = "unary",
                IsUnary = true
            },
            new OperationMetadata
            {
                Name = "ABS",
                Symbol = "|x|",
                Description = "Calculate the absolute value of the current result",
                MinValues = 0,
                MaxValues = 0,
                Category = "basic",
                Behavior = "unary",
                IsUnary = true
            },
            new OperationMetadata
            {
                Name = "ROUND",
                Symbol = "round",
                Description = "Round the current result to specified decimal places (default: 0)",
                MinValues = 0,
                MaxValues = 1,
                Category = "basic",
                Behavior = "unary-optional",
                IsUnary = true
            },
            new OperationMetadata
            {
                Name = "FLOOR",
                Symbol = "⌊x⌋",
                Description = "Round down the current result to the nearest integer",
                MinValues = 0,
                MaxValues = 0,
                Category = "basic",
                Behavior = "unary",
                IsUnary = true
            },
            new OperationMetadata
            {
                Name = "CEIL",
                Symbol = "⌈x⌉",
                Description = "Round up the current result to the nearest integer",
                MinValues = 0,
                MaxValues = 0,
                Category = "basic",
                Behavior = "unary",
                IsUnary = true
            },

            // Multi-value operations
            new OperationMetadata
            {
                Name = "MIN",
                Symbol = "min",
                Description = "Return the minimum value between the current result and provided values",
                MinValues = 1,
                MaxValues = -1,
                Category = "multi-value",
                Behavior = "accumulator",
                IsUnary = false
            },
            new OperationMetadata
            {
                Name = "MAX",
                Symbol = "max",
                Description = "Return the maximum value between the current result and provided values",
                MinValues = 1,
                MaxValues = -1,
                Category = "multi-value",
                Behavior = "accumulator",
                IsUnary = false
            },
            new OperationMetadata
            {
                Name = "CLAMP",
                Symbol = "clamp",
                Description = "Clamp the current result between min and max values",
                MinValues = 2,
                MaxValues = 2,
                Category = "multi-value",
                Behavior = "unary",
                IsUnary = true
            },

            // Special operations
            new OperationMetadata
            {
                Name = "SET",
                Symbol = "=",
                Description = "Set the accumulator to a specific value (ignores previous result)",
                MinValues = 1,
                MaxValues = 1,
                Category = "special",
                Behavior = "set",
                IsUnary = false
            }
        };
    }
}
