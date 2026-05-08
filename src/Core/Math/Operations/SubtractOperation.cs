using System;

namespace Core.Math.Operations;

/// <summary>
/// Subtraction operation strategy. Subtracts second operand from first.
/// </summary>
public class SubtractOperation : IOperationStrategy
{
    public string OperationName => "SUBTRACT";

    public void ValidateOperandCount(int operandCount)
    {
        if (operandCount != 2)
            throw new ArgumentException($"SUBTRACT requires exactly 2 operands, got {operandCount}");
    }

    public float Evaluate(float[] operands)
    {
        return operands[0] - operands[1];
    }
}
