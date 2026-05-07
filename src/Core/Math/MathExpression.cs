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

        public MathExpression Log(float? baseValue = null)
        {
            if (baseValue.HasValue && (baseValue.Value <= 0 || baseValue.Value == 1))
                throw new ArgumentException($"Logarithm base must be positive and not equal to 1. Got: {baseValue.Value}", nameof(baseValue));
            
            float[] values = baseValue.HasValue 
                ? new float[] { baseValue.Value } 
                : Array.Empty<float>();
            _steps.Add(new MathStep("LOG", values));
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

        public MathExpression Min(params float[] values)
        {
            _steps.Add(new MathStep("MIN", values));
            return this;
        }

        public MathExpression Max(params float[] values)
        {
            _steps.Add(new MathStep("MAX", values));
            return this;
        }

        public MathExpression Abs()
        {
            _steps.Add(new MathStep("ABS", Array.Empty<float>()));
            return this;
        }

        public MathExpression Pow(float exponent)
        {
            _steps.Add(new MathStep("POW", new float[] { exponent }));
            return this;
        }

        public MathExpression Round(int decimals = 0)
        {
            _steps.Add(new MathStep("ROUND", new float[] { decimals }));
            return this;
        }

        public MathExpression Floor()
        {
            _steps.Add(new MathStep("FLOOR", Array.Empty<float>()));
            return this;
        }

        public MathExpression Ceil()
        {
            _steps.Add(new MathStep("CEIL", Array.Empty<float>()));
            return this;
        }

        /// <summary>
        /// Define o valor do acumulador, ignorando o valor anterior.
        /// Usado para operações com operandos explícitos que calculam um resultado independente.
        /// </summary>
        public MathExpression Set(float value)
        {
            _steps.Add(new MathStep("SET", new float[] { value }));
            return this;
        }

        // Método genérico para adicionar steps vindos de APIs ou fontes externas (modo implícito)
        public MathExpression AddRawStep(string operation, float[] values)
        {
            _steps.Add(new MathStep(operation, values));
            return this;
        }

        // Método para adicionar steps com operandos explícitos (novo sistema híbrido)
        // Nota: Este método NÃO resolve os operandos - apenas armazena como literais numéricos
        // A resolução de "$current", "params.X", etc. deve ser feita pelo MathEngine antes de chamar este método
        public MathExpression AddRawStepWithOperands(string operation, List<string> operands)
        {
            // Converte operandos string para float[] para armazenamento interno
            // Assume que os operandos já foram resolvidos para valores numéricos
            var values = operands.Select(op => 
            {
                if (float.TryParse(op, System.Globalization.NumberStyles.Float, 
                    System.Globalization.CultureInfo.InvariantCulture, out float val))
                {
                    return val;
                }
                throw new ArgumentException($"Operand '{op}' must be a numeric literal when using AddRawStepWithOperands. " +
                    "Resolve '$current', '$initial', and 'params.X' references before calling this method.");
            }).ToArray();
            
            _steps.Add(new MathStep(operation, values));
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
                        
                        if (step.Values.Length == 0)
                        {
                            // Logaritmo natural (base e)
                            currentValue = (float)SysMath.Log(currentValue);
                        }
                        else
                        {
                            float logBase = step.Values[0];
                            if (logBase == 10f)
                            {
                                // Otimização: usar Log10 diretamente
                                currentValue = (float)SysMath.Log10(currentValue);
                            }
                            else
                            {
                                // Logaritmo com base customizada: log_b(x) = ln(x) / ln(b)
                                currentValue = (float)(SysMath.Log(currentValue) / SysMath.Log(logBase));
                            }
                        }
                        
                        ValidateResult(currentValue, step.Operation);
                        break;

                    case "NEGATE":
                        currentValue = currentValue * -1;
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

                    case "MIN":
                        if (step.Values.Length == 0)
                            throw new InvalidOperationException("MIN requires at least one value.");
                        foreach (var v in step.Values)
                        {
                            currentValue = SysMath.Min(currentValue, v);
                        }
                        break;

                    case "MAX":
                        if (step.Values.Length == 0)
                            throw new InvalidOperationException("MAX requires at least one value.");
                        foreach (var v in step.Values)
                        {
                            currentValue = SysMath.Max(currentValue, v);
                        }
                        break;

                    case "ABS":
                        currentValue = SysMath.Abs(currentValue);
                        break;

                    case "ROUND":
                        int decimals = step.Values.Length > 0 ? (int)step.Values[0] : 0;
                        currentValue = (float)SysMath.Round(currentValue, decimals);
                        break;

                    case "FLOOR":
                        currentValue = (float)SysMath.Floor(currentValue);
                        break;

                    case "CEIL":
                        currentValue = (float)SysMath.Ceiling(currentValue);
                        break;

                    case "SET":
                        if (step.Values.Length != 1)
                            throw new InvalidOperationException("SET requires exactly one value.");
                        currentValue = step.Values[0];
                        ValidateResult(currentValue, step.Operation);
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