using System;

namespace Core.Math.Operations;

/// <summary>
/// Division operation strategy. Divides first operand by second.
/// </summary>
public class DivideOperation : IOperationStrategy
{
    public string OperationName => "DIVIDE";

    public void ValidateOperandCount(int operandCount)
    {
        if (operandCount != 2)
            throw new ArgumentException($"DIVIDE requires exactly 2 operands, got {operandCount}");
    }

    public float Evaluate(float[] operands)
    {
        if (operands[1] == 0)
            throw new DivideByZeroException("Cannot divide by zero");
        return operands[0] / operands[1];
    }
}
