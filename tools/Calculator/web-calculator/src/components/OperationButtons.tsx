import { useState } from 'react';
import { Button } from './ui/Button';
import { Input } from './ui/Input';
import { OPERATIONS, MathOperation } from '../types/api';
import type { ApiMode } from '../types/api';

interface OperationButtonsProps {
  mode: ApiMode;
  parameters?: Record<string, number>;
  onAddStep: (operation: string, values?: number[], operands?: string[]) => void;
  disabled?: boolean;
}

export function OperationButtons({ mode, parameters, onAddStep, disabled }: OperationButtonsProps) {
  const [inputValues, setInputValues] = useState<Record<string, string>>({});
  const [errors, setErrors] = useState<Record<string, string>>({});

  const validateSymbolicOperand = (operand: string): string | null => {
    const trimmed = operand.trim();
    
    // Check for special symbols
    if (trimmed === '$current' || trimmed === '$initial') {
      return null;
    }
    
    // Check for params reference
    if (trimmed.startsWith('params.')) {
      const paramName = trimmed.substring(7);
      if (!parameters || parameters[paramName] === undefined) {
        return `Parameter '${paramName}' not defined`;
      }
      return null;
    }
    
    // Check if it's a numeric literal
    if (!isNaN(parseFloat(trimmed))) {
      return null;
    }
    
    return `Invalid operand: '${trimmed}'. Use $current, $initial, params.NAME, or a number`;
  };

  const handleOperation = (operation: MathOperation, minValues: number, maxValues: number) => {
    setErrors({});

    // Operations with no values (SQRT, ABS, NEGATE, FLOOR, CEIL)
    if (minValues === 0 && maxValues === 0) {
      if (mode === 'implicit') {
        onAddStep(operation, []);
      } else {
        onAddStep(operation, undefined, []);
      }
      return;
    }

    // Get input value(s) for this operation
    const inputValue = inputValues[operation] || '';
    
    if (!inputValue.trim()) {
      setErrors({ [operation]: `Please enter value(s) for ${operation}` });
      return;
    }

    // Mode 1: Implicit (Values)
    if (mode === 'implicit') {
      const values = inputValue
        .split(',')
        .map(v => parseFloat(v.trim()))
        .filter(v => !isNaN(v));

      if (values.length === 0) {
        setErrors({ [operation]: 'Please enter valid number(s)' });
        return;
      }

      if (values.length < minValues) {
        setErrors({ [operation]: `${operation} requires at least ${minValues} value(s)` });
        return;
      }

      if (maxValues !== Infinity && values.length > maxValues) {
        setErrors({ [operation]: `${operation} accepts at most ${maxValues} value(s)` });
        return;
      }

      onAddStep(operation, values);
      setInputValues({ ...inputValues, [operation]: '' });
      return;
    }

    // Mode 2: Explicit Literal (Operands as numeric strings)
    if (mode === 'explicit-literal') {
      const operands = inputValue
        .split(',')
        .map(v => v.trim())
        .filter(v => v.length > 0);

      // Validate all are numeric
      const allNumeric = operands.every(op => !isNaN(parseFloat(op)));
      if (!allNumeric) {
        setErrors({ [operation]: 'All operands must be numeric in Explicit Literal mode' });
        return;
      }

      if (operands.length === 0) {
        setErrors({ [operation]: 'Please enter valid operand(s)' });
        return;
      }

      if (operands.length < minValues) {
        setErrors({ [operation]: `${operation} requires at least ${minValues} operand(s)` });
        return;
      }

      if (maxValues !== Infinity && operands.length > maxValues) {
        setErrors({ [operation]: `${operation} accepts at most ${maxValues} operand(s)` });
        return;
      }

      onAddStep(operation, undefined, operands);
      setInputValues({ ...inputValues, [operation]: '' });
      return;
    }

    // Mode 3: Explicit Symbolic (Operands with $current, $initial, params.X)
    if (mode === 'explicit-symbolic') {
      const operands = inputValue
        .split(',')
        .map(v => v.trim())
        .filter(v => v.length > 0);

      if (operands.length === 0) {
        setErrors({ [operation]: 'Please enter valid operand(s)' });
        return;
      }

      // Validate each operand
      for (const operand of operands) {
        const error = validateSymbolicOperand(operand);
        if (error) {
          setErrors({ [operation]: error });
          return;
        }
      }

      if (operands.length < minValues) {
        setErrors({ [operation]: `${operation} requires at least ${minValues} operand(s)` });
        return;
      }

      if (maxValues !== Infinity && operands.length > maxValues) {
        setErrors({ [operation]: `${operation} accepts at most ${maxValues} operand(s)` });
        return;
      }

      onAddStep(operation, undefined, operands);
      setInputValues({ ...inputValues, [operation]: '' });
      return;
    }
  };

  const getPlaceholder = (op: MathOperation): string => {
    if (mode === 'implicit') {
      if (op === MathOperation.CLAMP) return 'Min, Max (e.g., 0, 100)';
      return 'Value(s) (comma-separated)';
    }
    
    if (mode === 'explicit-literal') {
      if (op === MathOperation.CLAMP) return '"0", "100"';
      return 'Numeric strings (e.g., "10", "20")';
    }
    
    // Mode 3: Symbolic
    if (op === MathOperation.CLAMP) return 'params.MIN, params.MAX';
    return '$current, params.X, or numbers';
  };

  const basicOps = OPERATIONS.filter(op => op.category === 'basic');
  const advancedOps = OPERATIONS.filter(op => op.category === 'advanced');
  const multiValueOps = OPERATIONS.filter(op => op.category === 'multi-value');

  return (
    <div className="space-y-6">
      {/* Basic Operations */}
      <div>
        <h3 className="text-sm font-semibold text-gray-700 dark:text-gray-300 mb-3">
          Basic Operations
        </h3>
        <div className="grid grid-cols-2 gap-3">
          {basicOps.map(op => (
            <div key={op.operation} className="space-y-2">
              <Input
                type="text"
                placeholder={getPlaceholder(op.operation)}
                value={inputValues[op.operation] || ''}
                onChange={(e) => {
                  setInputValues({ ...inputValues, [op.operation]: e.target.value });
                  setErrors({ ...errors, [op.operation]: '' });
                }}
                disabled={disabled}
                className="text-center"
              />
              <Button
                onClick={() => handleOperation(op.operation, op.minValues, op.maxValues)}
                disabled={disabled}
                variant="outline"
                className="w-full"
                title={op.description}
              >
                <span className="text-lg font-semibold mr-2">{op.symbol}</span>
                {op.label}
              </Button>
              {errors[op.operation] && (
                <div className="text-xs text-red-600 dark:text-red-400">
                  {errors[op.operation]}
                </div>
              )}
            </div>
          ))}
        </div>
      </div>

      {/* Advanced Operations */}
      <div>
        <h3 className="text-sm font-semibold text-gray-700 dark:text-gray-300 mb-3">
          Advanced Operations
        </h3>
        <div className="grid grid-cols-2 sm:grid-cols-3 gap-3">
          {advancedOps.map(op => {
            const needsInput = op.minValues > 0;
            return (
              <div key={op.operation} className="space-y-2">
                {needsInput && (
                  <Input
                    type="text"
                    placeholder={getPlaceholder(op.operation)}
                    value={inputValues[op.operation] || ''}
                    onChange={(e) => {
                      setInputValues({ ...inputValues, [op.operation]: e.target.value });
                      setErrors({ ...errors, [op.operation]: '' });
                    }}
                    disabled={disabled}
                    className="text-center"
                  />
                )}
                <Button
                  onClick={() => handleOperation(op.operation, op.minValues, op.maxValues)}
                  disabled={disabled}
                  variant="secondary"
                  className="w-full"
                  title={op.description}
                >
                  <span className="text-lg font-semibold mr-2">{op.symbol}</span>
                  {op.label}
                </Button>
                {errors[op.operation] && (
                  <div className="text-xs text-red-600 dark:text-red-400">
                    {errors[op.operation]}
                  </div>
                )}
              </div>
            );
          })}
        </div>
      </div>

      {/* Multi-Value Operations */}
      <div>
        <h3 className="text-sm font-semibold text-gray-700 dark:text-gray-300 mb-3">
          Multi-Value Operations
        </h3>
        <div className="space-y-3">
          {multiValueOps.map(op => (
            <div key={op.operation} className="space-y-2">
              <Input
                type="text"
                placeholder={getPlaceholder(op.operation)}
                value={inputValues[op.operation] || ''}
                onChange={(e) => {
                  setInputValues({ ...inputValues, [op.operation]: e.target.value });
                  setErrors({ ...errors, [op.operation]: '' });
                }}
                disabled={disabled}
              />
              <Button
                onClick={() => handleOperation(op.operation, op.minValues, op.maxValues)}
                disabled={disabled}
                variant="outline"
                className="w-full"
                title={op.description}
              >
                <span className="text-sm font-semibold mr-2">{op.symbol}</span>
                {op.label}
              </Button>
              {errors[op.operation] && (
                <div className="text-xs text-red-600 dark:text-red-400">
                  {errors[op.operation]}
                </div>
              )}
            </div>
          ))}
        </div>
      </div>
    </div>
  );
}
