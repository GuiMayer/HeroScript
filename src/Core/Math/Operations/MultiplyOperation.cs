using System;

namespace Core.Math.Operations;

/// <summary>
/// Multiplication operation strategy. Multiplies all operands.
/// </summary>
public class MultiplyOperation : IOperationStrategy
{
    public string OperationName => "MULTIPLY";

    public void ValidateOperandCount(int operandCount)
    {
        if (operandCount < 2)
            throw new ArgumentException($"MULTIPLY requires at least 2 operands, got {operandCount}");
    }

    public float Evaluate(float[] operands)
    {
        float product = operands[0];
        for (int i = 1; i < operands.Length; i++)
            product *= operands[i];
        return product;
    }
}
