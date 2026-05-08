using System;
using System.Collections.Generic;

namespace Core.Math.Operations;

/// <summary>
/// Registry for operation strategies. Provides lookup and execution of operations.
/// </summary>
public class OperationRegistry
{
    private readonly Dictionary<string, IOperationStrategy> _operations;

    public OperationRegistry()
    {
        _operations = new Dictionary<string, IOperationStrategy>(StringComparer.OrdinalIgnoreCase);
        RegisterDefaultOperations();
    }

    /// <summary>
    /// Registers all default operations.
    /// </summary>
    private void RegisterDefaultOperations()
    {
        Register(new AddOperation());
        Register(new SubtractOperation());
        Register(new MultiplyOperation());
        Register(new DivideOperation());
    }

    /// <summary>
    /// Registers an operation strategy.
    /// </summary>
    public void Register(IOperationStrategy operation)
    {
        _operations[operation.OperationName] = operation;
    }

    /// <summary>
    /// Evaluates an operation with the given operands.
    /// </summary>
    /// <param name="operationName">Name of the operation (e.g., "ADD")</param>
    /// <param name="operands">Array of operand values</param>
    /// <returns>Result of the operation</returns>
    /// <exception cref="NotSupportedException">Thrown if operation is not registered</exception>
    public float Evaluate(string operationName, float[] operands)
    {
        if (!_operations.TryGetValue(operationName, out var operation))
            throw new NotSupportedException($"Operation '{operationName}' is not supported");

        operation.ValidateOperandCount(operands.Length);
        return operation.Evaluate(operands);
    }

    /// <summary>
    /// Checks if an operation is registered.
    /// </summary>
    public bool IsRegistered(string operationName)
    {
        return _operations.ContainsKey(operationName);
    }
}
