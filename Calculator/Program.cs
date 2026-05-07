using System;
using Core.Math;

namespace Calculator
{
    class Program
    {
        static void Main(string[] args)
        {
            Console.WriteLine("===========================================");
            Console.WriteLine("  MathExpression Calculator");
            Console.WriteLine("===========================================");
            Console.WriteLine();
            Console.WriteLine("Commands:");
            Console.WriteLine("  start <value>     - Start with initial value");
            Console.WriteLine("  continue          - Continue with last result (alias: cont, c)");
            Console.WriteLine("  add <values>      - Add values (e.g., add 5 10 15)");
            Console.WriteLine("  sub <values>      - Subtract values");
            Console.WriteLine("  mul <values>      - Multiply by values");
            Console.WriteLine("  div <values>      - Divide by values");
            Console.WriteLine("  divinv <value>    - Inverse division (value / current)");
            Console.WriteLine("  pow <values>      - Power (current ^ values)");
            Console.WriteLine("  powbase <value>   - Power base (value ^ current)");
            Console.WriteLine("  sqrt              - Square root");
            Console.WriteLine("  log               - Natural logarithm");
            Console.WriteLine("  neg               - Negate");
            Console.WriteLine("  clamp <min> <max> - Clamp between min and max");
            Console.WriteLine("  build             - Execute and show result");
            Console.WriteLine("  steps             - Show current steps");
            Console.WriteLine("  reset             - Reset calculator");
            Console.WriteLine("  exit              - Exit calculator");
            Console.WriteLine();
            Console.WriteLine("===========================================");
            Console.WriteLine();

            MathExpression? expr = null;
            float? initialValue = null;
            float? lastResult = null;
            bool isFromContinue = false;
            bool running = true;

            while (running)
            {
                Console.Write("> ");
                string? input = Console.ReadLine();
                
                if (string.IsNullOrWhiteSpace(input))
                    continue;

                string[] parts = input.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
                string command = parts[0].ToLower();

                try
                {
                    switch (command)
                    {
                        case "start":
                            if (parts.Length < 2)
                            {
                                Console.WriteLine("Error: start requires a value");
                                break;
                            }
                            float startValue = float.Parse(parts[1]);
                            expr = new MathExpression(startValue);
                            initialValue = startValue;
                            isFromContinue = false;
                            Console.WriteLine($"Started with: {startValue}");
                            break;

                        case "continue":
                        case "cont":
                        case "c":
                            if (lastResult == null)
                            {
                                Console.WriteLine("Error: No previous result. Use 'build' first");
                                break;
                            }
                            expr = new MathExpression(lastResult.Value);
                            initialValue = lastResult.Value;
                            isFromContinue = true;
                            Console.WriteLine($"Continuing with last result: {lastResult.Value}");
                            break;

                        case "add":
                            if (expr == null)
                            {
                                Console.WriteLine("Error: Use 'start <value>' first");
                                break;
                            }
                            if (parts.Length < 2)
                            {
                                Console.WriteLine("Error: add requires at least one value");
                                break;
                            }
                            float[] addValues = ParseValues(parts, 1);
                            expr.Add(addValues);
                            Console.WriteLine($"Added: {string.Join(", ", addValues)}");
                            break;

                        case "sub":
                            if (expr == null)
                            {
                                Console.WriteLine("Error: Use 'start <value>' first");
                                break;
                            }
                            if (parts.Length < 2)
                            {
                                Console.WriteLine("Error: sub requires at least one value");
                                break;
                            }
                            float[] subValues = ParseValues(parts, 1);
                            expr.Subtract(subValues);
                            Console.WriteLine($"Subtracted: {string.Join(", ", subValues)}");
                            break;

                        case "mul":
                            if (expr == null)
                            {
                                Console.WriteLine("Error: Use 'start <value>' first");
                                break;
                            }
                            if (parts.Length < 2)
                            {
                                Console.WriteLine("Error: mul requires at least one value");
                                break;
                            }
                            float[] mulValues = ParseValues(parts, 1);
                            expr.Multiply(mulValues);
                            Console.WriteLine($"Multiplied by: {string.Join(", ", mulValues)}");
                            break;

                        case "div":
                            if (expr == null)
                            {
                                Console.WriteLine("Error: Use 'start <value>' first");
                                break;
                            }
                            if (parts.Length < 2)
                            {
                                Console.WriteLine("Error: div requires at least one value");
                                break;
                            }
                            float[] divValues = ParseValues(parts, 1);
                            expr.Divide(divValues);
                            Console.WriteLine($"Divided by: {string.Join(", ", divValues)}");
                            break;

                        case "divinv":
                            if (expr == null)
                            {
                                Console.WriteLine("Error: Use 'start <value>' first");
                                break;
                            }
                            if (parts.Length < 2)
                            {
                                Console.WriteLine("Error: divinv requires a value");
                                break;
                            }
                            float divInvValue = float.Parse(parts[1]);
                            expr.DivideInverse(divInvValue);
                            Console.WriteLine($"Inverse division: {divInvValue} / current");
                            break;

                        case "pow":
                            if (expr == null)
                            {
                                Console.WriteLine("Error: Use 'start <value>' first");
                                break;
                            }
                            if (parts.Length < 2)
                            {
                                Console.WriteLine("Error: pow requires at least one value");
                                break;
                            }
                            float[] powValues = ParseValues(parts, 1);
                            expr.Pow(powValues);
                            Console.WriteLine($"Power: current ^ {string.Join(", ", powValues)}");
                            break;

                        case "powbase":
                            if (expr == null)
                            {
                                Console.WriteLine("Error: Use 'start <value>' first");
                                break;
                            }
                            if (parts.Length < 2)
                            {
                                Console.WriteLine("Error: powbase requires a value");
                                break;
                            }
                            float powBaseValue = float.Parse(parts[1]);
                            expr.PowBase(powBaseValue);
                            Console.WriteLine($"Power base: {powBaseValue} ^ current");
                            break;

                        case "sqrt":
                            if (expr == null)
                            {
                                Console.WriteLine("Error: Use 'start <value>' first");
                                break;
                            }
                            expr.Sqrt();
                            Console.WriteLine("Square root applied");
                            break;

                        case "log":
                            if (expr == null)
                            {
                                Console.WriteLine("Error: Use 'start <value>' first");
                                break;
                            }
                            
                            if (parts.Length == 1)
                            {
                                expr.Log();
                                Console.WriteLine("Natural logarithm applied");
                            }
                            else if (parts.Length == 2)
                            {
                                float baseValue = float.Parse(parts[1]);
                                expr.Log(baseValue);
                                Console.WriteLine($"Logarithm base {baseValue} applied");
                            }
                            else
                            {
                                Console.WriteLine("Error: log accepts 0 or 1 argument: log [base]");
                            }
                            break;

                        case "neg":
                            if (expr == null)
                            {
                                Console.WriteLine("Error: Use 'start <value>' first");
                                break;
                            }
                            expr.Negate();
                            Console.WriteLine("Negated");
                            break;

                        case "clamp":
                            if (expr == null)
                            {
                                Console.WriteLine("Error: Use 'start <value>' first");
                                break;
                            }
                            if (parts.Length < 3)
                            {
                                Console.WriteLine("Error: clamp requires min and max values");
                                break;
                            }
                            float min = float.Parse(parts[1]);
                            float max = float.Parse(parts[2]);
                            expr.Clamp(min, max);
                            Console.WriteLine($"Clamped between {min} and {max}");
                            break;

                        case "build":
                            if (expr == null)
                            {
                                Console.WriteLine("Error: Use 'start <value>' first");
                                break;
                            }
                            
                            Console.WriteLine();
                            Console.WriteLine("========================================");
                            
                            // Mostrar valor inicial com origem
                            if (isFromContinue)
                                Console.WriteLine($"  0. CONTINUE {initialValue}");
                            else
                                Console.WriteLine($"  0. START {initialValue}");
                            
                            // Executar step by step e mostrar resultados intermediários
                            float currentValue = initialValue.Value;
                            var steps = expr.GetSteps();
                            bool hasError = false;
                            
                            for (int i = 0; i < steps.Count; i++)
                            {
                                var step = steps[i];
                                string valuesStr = step.Values.Length > 0 
                                    ? $"[{string.Join(", ", step.Values)}]" 
                                    : "";
                                
                                try
                                {
                                    currentValue = SimulateStep(currentValue, step);
                                    Console.WriteLine($"  {i + 1}. {step.Operation} {valuesStr} → {currentValue}");
                                }
                                catch (Exception ex)
                                {
                                    Console.WriteLine($"  {i + 1}. {step.Operation} {valuesStr} → ERROR: {ex.Message}");
                                    hasError = true;
                                    break;
                                }
                            }
                            
                            if (!hasError)
                            {
                                // Executar build real para validar
                                float result = expr.Build();
                                Console.WriteLine("========================================");
                                Console.WriteLine($"  RESULT: {result}");
                                Console.WriteLine("========================================");
                                Console.WriteLine();
                                
                                // Salvar resultado
                                lastResult = result;
                            }
                            else
                            {
                                Console.WriteLine("========================================");
                                Console.WriteLine("  BUILD FAILED");
                                Console.WriteLine("========================================");
                                Console.WriteLine();
                            }
                            
                            // Reset after build
                            expr = null;
                            initialValue = null;
                            isFromContinue = false;
                            break;

                        case "steps":
                            if (expr == null)
                            {
                                Console.WriteLine("No expression started. Use 'start <value>' first");
                                break;
                            }
                            var currentSteps = expr.GetSteps();
                            Console.WriteLine();
                            Console.WriteLine("Current expression:");
                            if (isFromContinue)
                                Console.WriteLine($"  0. CONTINUE with {initialValue} (from last build)");
                            else
                                Console.WriteLine($"  0. START with {initialValue} (fresh)");
                            if (currentSteps.Count == 0)
                            {
                                Console.WriteLine("  (no operations yet)");
                            }
                            else
                            {
                                for (int i = 0; i < currentSteps.Count; i++)
                                {
                                    var step = currentSteps[i];
                                    string valuesStr = step.Values.Length > 0 
                                        ? $"[{string.Join(", ", step.Values)}]" 
                                        : "[]";
                                    Console.WriteLine($"  {i + 1}. {step.Operation} {valuesStr}");
                                }
                            }
                            Console.WriteLine();
                            break;

                        case "reset":
                            expr = null;
                            initialValue = null;
                            lastResult = null;
                            isFromContinue = false;
                            Console.WriteLine("Calculator reset");
                            break;

                        case "exit":
                            running = false;
                            Console.WriteLine("Goodbye!");
                            break;

                        case "help":
                            Console.WriteLine();
                            Console.WriteLine("Available commands:");
                            Console.WriteLine("  start, continue (cont/c), add, sub, mul, div, divinv");
                            Console.WriteLine("  pow, powbase, sqrt, log, neg, clamp");
                            Console.WriteLine("  build, steps, reset, exit");
                            Console.WriteLine();
                            break;

                        default:
                            Console.WriteLine($"Unknown command: {command}. Type 'help' for available commands");
                            break;
                    }
                }
                catch (FormatException)
                {
                    Console.WriteLine("Error: Invalid number format");
                }
                catch (DivideByZeroException ex)
                {
                    Console.WriteLine($"Error: {ex.Message}");
                    expr = null;
                }
                catch (InvalidOperationException ex)
                {
                    Console.WriteLine($"Error: {ex.Message}");
                    expr = null;
                }
                catch (ArgumentException ex)
                {
                    Console.WriteLine($"Error: {ex.Message}");
                    expr = null;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Unexpected error: {ex.Message}");
                    expr = null;
                }
            }
        }

        static float[] ParseValues(string[] parts, int startIndex)
        {
            float[] values = new float[parts.Length - startIndex];
            for (int i = startIndex; i < parts.Length; i++)
            {
                values[i - startIndex] = float.Parse(parts[i]);
            }
            return values;
        }

        static float SimulateStep(float currentValue, Core.Math.MathStep step)
        {
            switch (step.Operation.ToUpper())
            {
                case "ADD":
                    foreach (var v in step.Values) currentValue += v;
                    break;
                case "SUBTRACT":
                    foreach (var v in step.Values) currentValue -= v;
                    break;
                case "MULTIPLY":
                    if (step.Values.Length == 0)
                        throw new InvalidOperationException("MULTIPLY requires at least one value.");
                    foreach (var v in step.Values) currentValue *= v;
                    break;
                case "DIVIDE":
                    if (step.Values.Length == 0)
                        throw new InvalidOperationException("DIVIDE requires at least one value.");
                    foreach (var v in step.Values)
                    {
                        if (v == 0) throw new DivideByZeroException("Cannot divide by zero.");
                        currentValue /= v;
                    }
                    break;
                case "DIVIDE_INVERSE":
                    if (step.Values.Length == 0)
                        throw new InvalidOperationException("DIVIDE_INVERSE requires exactly one value.");
                    float numerator = step.Values[0];
                    if (currentValue == 0)
                        throw new DivideByZeroException("Cannot divide by zero (currentValue is zero in inverse division).");
                    currentValue = numerator / currentValue;
                    break;
                case "POW":
                    if (step.Values.Length == 0)
                        throw new InvalidOperationException("POW requires at least one value.");
                    foreach (var v in step.Values) currentValue = (float)System.Math.Pow(currentValue, v);
                    break;
                case "POW_BASE":
                    if (step.Values.Length == 0)
                        throw new InvalidOperationException("POW_BASE requires exactly one value.");
                    float baseValue = step.Values[0];
                    currentValue = (float)System.Math.Pow(baseValue, currentValue);
                    break;
                case "SQRT":
                    if (currentValue < 0) throw new InvalidOperationException("Cannot take the square root of a negative number.");
                    currentValue = (float)System.Math.Sqrt(currentValue);
                    break;
                case "LOG":
                    if (currentValue <= 0) throw new InvalidOperationException("Cannot take the logarithm of a non-positive number.");
                    
                    if (step.Values.Length == 0)
                    {
                        // Logaritmo natural (base e)
                        currentValue = (float)System.Math.Log(currentValue);
                    }
                    else
                    {
                        float logBase = step.Values[0];
                        if (logBase == 10f)
                        {
                            // Otimização: usar Log10 diretamente
                            currentValue = (float)System.Math.Log10(currentValue);
                        }
                        else
                        {
                            // Logaritmo com base customizada: log_b(x) = ln(x) / ln(b)
                            currentValue = (float)(System.Math.Log(currentValue) / System.Math.Log(logBase));
                        }
                    }
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
                    currentValue = System.Math.Clamp(currentValue, min, max);
                    break;
                default:
                    throw new InvalidOperationException($"Unknown operation: {step.Operation}");
            }
            
            // Validar resultado
            if (float.IsNaN(currentValue))
                throw new InvalidOperationException($"Operation {step.Operation} resulted in NaN (Not a Number).");
            if (float.IsInfinity(currentValue))
                throw new InvalidOperationException($"Operation {step.Operation} resulted in Infinity.");
            
            return currentValue;
        }
    }
}
