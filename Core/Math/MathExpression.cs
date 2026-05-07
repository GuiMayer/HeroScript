using System;
using System.Collections.Generic;
using SysMath = System.Math;

namespace Core.Math
{
    // 1. O "Log" Imutável
    public record MathStep(string Operation, float[] Values);

    public class MathExpression
    {
        private readonly float _initialValue;

        // A "receita" do cálculo
        private readonly List<MathStep> _steps = new();

        public MathExpression(float initialValue)
        {
            _initialValue = initialValue;
        }

        public MathExpression()
        {
            _initialValue = 0f;
        }

        // 3. REGISTRO DE INTENÇÕES (Nenhuma conta é feita aqui)
        public MathExpression Add(params float[] values)
        {
            _steps.Add(new MathStep("ADD", values));
            return this;
        }

        public MathExpression Subtract(params float[] values)
        {
            _steps.Add(new MathStep("SUBTRACT", values));
            return this;
        }

        public MathExpression Multiply(params float[] values)
        {
            _steps.Add(new MathStep("MULTIPLY", values));
            return this;
        }

        public MathExpression Divide(params float[] values)
        {
            _steps.Add(new MathStep("DIVIDE", values));
            return this;
        }

        public MathExpression DivideInverse(float numerator)
        {
            if (numerator == 0)
                throw new ArgumentException("Numerator cannot be zero in inverse division.", nameof(numerator));
            _steps.Add(new MathStep("DIVIDE_INVERSE", new float[] { numerator }));
            return this;
        }

        public MathExpression Pow(params float[] values)
        {
            _steps.Add(new MathStep("POW", values));
            return this;
        }

        public MathExpression PowBase(float baseValue)
        {
            _steps.Add(new MathStep("POW_BASE", new float[] { baseValue }));
            return this;
        }

        public MathExpression Sqrt()
        {
            _steps.Add(new MathStep("SQRT", Array.Empty<float>()));
            return this;
        }

        public MathExpression Log()
        {
            _steps.Add(new MathStep("LOG", Array.Empty<float>()));
            return this;
        }

        public MathExpression Negate()
        {
            _steps.Add(new MathStep("NEGATE", Array.Empty<float>()));
            return this;
        }

        public MathExpression Clamp(float min, float max)
        {
            if (min > max)
                throw new ArgumentException($"min ({min}) cannot be greater than max ({max}).", nameof(min));
            _steps.Add(new MathStep("CLAMP", new float[] { min, max }));
            return this;
        }

        // Método extra para a IA ou UI lerem o que está planejado
        public IReadOnlyList<MathStep> GetSteps() => _steps.AsReadOnly();

        // 4. O DESFECHO ONTOLÓGICO (O Grande Processador)
        public float Build()
        {
            float currentValue = _initialValue;

            foreach (var step in _steps)
            {
                switch (step.Operation.ToUpper())
                {
                    case "ADD":
                        foreach (var v in step.Values) currentValue += v;
                        ValidateResult(currentValue, step.Operation);
                        break;

                    case "SUBTRACT":
                        foreach (var v in step.Values) currentValue -= v;
                        ValidateResult(currentValue, step.Operation);
                        break;

                    case "MULTIPLY":
                        if (step.Values.Length == 0)
                            throw new InvalidOperationException("MULTIPLY requires at least one value.");
                        foreach (var v in step.Values) currentValue *= v;
                        ValidateResult(currentValue, step.Operation);
                        break;

                    case "DIVIDE":
                        if (step.Values.Length == 0)
                            throw new InvalidOperationException("DIVIDE requires at least one value.");
                        foreach (var v in step.Values)
                        {
                            if (v == 0) throw new DivideByZeroException("Cannot divide by zero.");
                            currentValue /= v;
                        }
                        ValidateResult(currentValue, step.Operation);
                        break;

                    case "DIVIDE_INVERSE":
                        if (step.Values.Length == 0)
                            throw new InvalidOperationException("DIVIDE_INVERSE requires exactly one value.");
                        float numerator = step.Values[0];
                        if (currentValue == 0)
                            throw new DivideByZeroException("Cannot divide by zero (currentValue is zero in inverse division).");
                        currentValue = numerator / currentValue;
                        ValidateResult(currentValue, step.Operation);
                        break;

                    case "POW":
                        if (step.Values.Length == 0)
                            throw new InvalidOperationException("POW requires at least one value.");
                        foreach (var v in step.Values) currentValue = (float)SysMath.Pow(currentValue, v);
                        ValidateResult(currentValue, step.Operation);
                        break;

                    case "POW_BASE":
                        if (step.Values.Length == 0)
                            throw new InvalidOperationException("POW_BASE requires exactly one value.");
                        float baseValue = step.Values[0];
                        currentValue = (float)SysMath.Pow(baseValue, currentValue);
                        ValidateResult(currentValue, step.Operation);
                        break;

                    case "SQRT":
                        if (currentValue < 0) throw new InvalidOperationException("Cannot take the square root of a negative number.");
                        currentValue = (float)SysMath.Sqrt(currentValue);
                        ValidateResult(currentValue, step.Operation);
                        break;

                    case "LOG":
                        if (currentValue <= 0) throw new InvalidOperationException("Cannot take the logarithm of a non-positive number.");
                        currentValue = (float)SysMath.Log(currentValue);
                        ValidateResult(currentValue, step.Operation);
                        break;

                    case "NEGATE":
                        currentValue = -currentValue;
                        break;

                    case "CLAMP":
                        if (step.Values.Length < 2)
                            throw new InvalidOperationException("CLAMP requires exactly 2 values (min, max).");
                        float min = step.Values[0];
                        float max = step.Values[1];
                        if (min > max)
                            throw new ArgumentException($"CLAMP min ({min}) cannot be greater than max ({max}).");
                        currentValue = SysMath.Clamp(currentValue, min, max);
                        break;

                    default:
                        throw new InvalidOperationException($"Unknown operation: {step.Operation}");
                }
            }

            return currentValue;
        }

        // Método auxiliar para validar resultados e detectar Infinity/NaN
        private void ValidateResult(float value, string operation)
        {
            if (float.IsNaN(value))
                throw new InvalidOperationException($"Operation {operation} resulted in NaN (Not a Number).");
            if (float.IsInfinity(value))
                throw new InvalidOperationException($"Operation {operation} resulted in Infinity.");
        }
    }
}