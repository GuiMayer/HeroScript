using System.Collections.Immutable;
using System.Globalization;

namespace Core.Math;

/// <summary>Static validation only: content publication never samples gameplay inputs or executes a formula.</summary>
public static class FormulaDefinitionValidator
{
    public static ImmutableArray<string> Validate(FormulaDefinition formula)
    {
        var errors = ImmutableArray.CreateBuilder<string>();
        if (formula.Params == null || formula.Operations == null) return ["params and operations cannot be null"];
        foreach (var (name, value) in formula.Params)
            if (string.IsNullOrWhiteSpace(name) || !float.IsFinite(value)) errors.Add("formula parameters require names and finite values");
        if (formula.Operations.Count > 4096) errors.Add("formula operation limit exceeded");
        foreach (var (operation, index) in formula.Operations.Take(4096).Select((operation, index) => (operation, index)))
        {
            void Error(string message) => errors.Add($"operations[{index}]: {message}");
            if (operation == null) { Error("operation cannot be null"); continue; }
            var op = operation.Op?.ToUpperInvariant() ?? string.Empty;
            if (operation.Operands is { Count: > 0 } operands)
            {
                if (operation.Value != null || operation.Min != null || operation.Max != null)
                    Error("explicit operands cannot be combined with value or bounds");
                var validArity = op switch
                {
                    "ADD" or "MULTIPLY" or "MIN" or "MAX" => operands.Count >= 2,
                    "SUBTRACT" or "DIVIDE" or "DIVIDE_INVERSE" or "POW" or "POW_BASE" or "MOD" or "MODULO" => operands.Count == 2,
                    "SQRT" or "NEGATE" or "ABS" or "FLOOR" or "CEIL" => operands.Count == 1,
                    "ROUND" or "LOG" => operands.Count is 1 or 2,
                    "CLAMP" => operands.Count == 3,
                    _ => false
                };
                if (!validArity) Error($"unsupported operation or operand count: {op}");
                foreach (var operand in operands) CheckValue(operand, allowAccumulator: true, Error);
            }
            else
            {
                switch (op)
                {
                    case "ADD": case "SUBTRACT": case "MULTIPLY": case "DIVIDE": case "DIVIDE_INVERSE":
                    case "POW": case "POW_BASE": case "MIN": case "MAX": case "SET": case "MOD": case "MODULO":
                        CheckValue(operation.Value, false, Error); break;
                    case "CLAMP":
                        CheckValue(operation.Min, false, Error); CheckValue(operation.Max, false, Error);
                        if (operation.Value != null) Error("CLAMP requires min/max, not value");
                        break;
                    case "ROUND":
                        if (operation.Value != null) CheckValue(operation.Value, false, Error);
                        break;
                    case "SQRT": case "LOG": case "NEGATE": case "ABS": case "FLOOR": case "CEIL":
                        if (operation.Value != null) Error($"{op} does not consume value");
                        break;
                    default: Error($"unsupported operation: {op}"); break;
                }
                if (op != "CLAMP" && (operation.Min != null || operation.Max != null)) Error($"{op} does not consume min/max");
            }
        }
        return errors.ToImmutable();

        void CheckValue(string? token, bool allowAccumulator, Action<string> error)
        {
            if (string.IsNullOrWhiteSpace(token)) { error("operand is required"); return; }
            if (allowAccumulator && (token.Equals("$current", StringComparison.OrdinalIgnoreCase) ||
                token.Equals("$initial", StringComparison.OrdinalIgnoreCase))) return;
            if (token.StartsWith("params.", StringComparison.OrdinalIgnoreCase) &&
                formula.Params.Keys.Contains(token[7..], StringComparer.OrdinalIgnoreCase)) return;
            if (float.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) && float.IsFinite(value)) return;
            error($"unknown parameter or invalid numeric operand: '{token}'");
        }
    }
}
