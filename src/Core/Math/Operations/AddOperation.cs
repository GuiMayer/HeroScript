using System;

namespace Core.Math.Operations;

/// <summary>
/// Addition operation strategy. Sums all operands.
/// </summary>
public class AddOperation : IOperationStrategy
{
    public string OperationName => "ADD";

    public void ValidateOperandCount(int operandCount)
    {
        if (operandCount < 2)
            throw new ArgumentException($"ADD requires at least 2 operands, got {operandCount}");
    }

    public float Evaluate(float[] operands)
    {
        float sum = operands[0];
        for (int i = 1; i < operands.Length; i++)
            sum += operands[i];
        return sum;
    }
}
