using System;

namespace Core.Math.Operations;

/// <summary>
/// Interface for operation strategies that evaluate explicit operands.
/// Each operation (ADD, MULTIPLY, etc.) implements this interface.
/// </summary>
public interface IOperationStrategy
{
    /// <summary>
    /// Gets the operation name (e.g., "ADD", "MULTIPLY").
    /// </summary>
    string OperationName { get; }

    /// <summary>
    /// Validates the number of operands for this operation.
    /// </summary>
    /// <param name="operandCount">Number of operands provided</param>
    /// <exception cref="ArgumentException">Thrown if operand count is invalid</exception>
    void ValidateOperandCount(int operandCount);

    /// <summary>
    /// Evaluates the operation with the given operands.
    /// </summary>
    /// <param name="operands">Array of operand values</param>
    /// <returns>Result of the operation</returns>
    float Evaluate(float[] operands);
}
