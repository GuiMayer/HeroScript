import { useState } from 'react';
import { Button } from './ui/Button';
import { Input } from './ui/Input';
import { OPERATIONS, MathOperation } from '../types/api';
import type { ApiMode } from '../types/api';

interface OperationsPanelProps {
  mode: ApiMode;
  parameters?: Record<string, number>;
  onAddStep: (operation: string, values?: number[], operands?: string[]) => void;
  disabled?: boolean;
}

type Category = 'basic' | 'advanced' | 'multi-value';

export function OperationsPanel({ mode, parameters, onAddStep, disabled }: OperationsPanelProps) {
  const [activeCategory, setActiveCategory] = useState<Category>('basic');
  const [inputValues, setInputValues] = useState<Record<string, string>>({});
  const [errors, setErrors] = useState<Record<string, string>>({});

  const validateSymbolicOperand = (operand: string): string | null => {
    const trimmed = operand.trim();
    
    if (trimmed === '$current' || trimmed === '$initial') {
      return null;
    }
    
    if (trimmed.startsWith('params.')) {
      const paramName = trimmed.substring(7);
      if (!parameters || parameters[paramName] === undefined) {
        return `Parameter '${paramName}' not defined`;
      }
      return null;
    }
    
    if (!isNaN(parseFloat(trimmed))) {
      return null;
    }
    
    return `Invalid operand: '${trimmed}'`;
  };

  const handleOperation = (operation: MathOperation, minValues: number, maxValues: number) => {
    setErrors({});

    if (minValues === 0 && maxValues === 0) {
      if (mode === 'implicit') {
        onAddStep(operation, []);
      } else {
        onAddStep(operation, undefined, []);
      }
      return;
    }

    const inputValue = inputValues[operation] || '';
    
    if (!inputValue.trim()) {
      setErrors({ [operation]: `Enter value(s)` });
      return;
    }

    if (mode === 'implicit') {
      const values = inputValue
        .split(',')
        .map(v => parseFloat(v.trim()))
        .filter(v => !isNaN(v));

      if (values.length === 0) {
        setErrors({ [operation]: 'Enter valid number(s)' });
        return;
      }

      if (values.length < minValues) {
        setErrors({ [operation]: `Need ${minValues}+ value(s)` });
        return;
      }

      if (maxValues !== Infinity && values.length > maxValues) {
        setErrors({ [operation]: `Max ${maxValues} value(s)` });
        return;
      }

      onAddStep(operation, values);
      setInputValues({ ...inputValues, [operation]: '' });
      return;
    }

    if (mode === 'explicit-literal') {
      const operands = inputValue
        .split(',')
        .map(v => v.trim())
        .filter(v => v.length > 0);

      const allNumeric = operands.every(op => !isNaN(parseFloat(op)));
      if (!allNumeric) {
        setErrors({ [operation]: 'Must be numeric' });
        return;
      }

      if (operands.length < minValues) {
        setErrors({ [operation]: `Need ${minValues}+ operand(s)` });
        return;
      }

      if (maxValues !== Infinity && operands.length > maxValues) {
        setErrors({ [operation]: `Max ${maxValues} operand(s)` });
        return;
      }

      onAddStep(operation, undefined, operands);
      setInputValues({ ...inputValues, [operation]: '' });
      return;
    }

    if (mode === 'explicit-symbolic') {
      const operands = inputValue
        .split(',')
        .map(v => v.trim())
        .filter(v => v.length > 0);

      if (operands.length === 0) {
        setErrors({ [operation]: 'Enter operand(s)' });
        return;
      }

      for (const operand of operands) {
        const error = validateSymbolicOperand(operand);
        if (error) {
          setErrors({ [operation]: error });
          return;
        }
      }

      if (operands.length < minValues) {
        setErrors({ [operation]: `Need ${minValues}+ operand(s)` });
        return;
      }

      if (maxValues !== Infinity && operands.length > maxValues) {
        setErrors({ [operation]: `Max ${maxValues} operand(s)` });
        return;
      }

      onAddStep(operation, undefined, operands);
      setInputValues({ ...inputValues, [operation]: '' });
      return;
    }
  };

  const getPlaceholder = (op: MathOperation): string => {
    if (mode === 'implicit') {
      if (op === MathOperation.CLAMP) return '0, 100';
      return 'e.g., 5';
    }
    
    if (mode === 'explicit-literal') {
      if (op === MathOperation.CLAMP) return '"0", "100"';
      return 'e.g., "10"';
    }
    
    if (op === MathOperation.CLAMP) return 'params.MIN, params.MAX';
    return '$current, params.X';
  };

  const categories = [
    { id: 'basic' as Category, label: 'Basic', count: OPERATIONS.filter(op => op.category === 'basic').length },
    { id: 'advanced' as Category, label: 'Advanced', count: OPERATIONS.filter(op => op.category === 'advanced').length },
    { id: 'multi-value' as Category, label: 'Multi-Value', count: OPERATIONS.filter(op => op.category === 'multi-value').length },
  ];

  const activeOps = OPERATIONS.filter(op => op.category === activeCategory);

  return (
    <div className="space-y-4">
      {/* Category Tabs */}
      <div className="flex gap-1 p-1 bg-gray-100 dark:bg-gray-800 rounded-lg">
        {categories.map((cat) => (
          <button
            key={cat.id}
            onClick={() => setActiveCategory(cat.id)}
            className={`
              flex-1 px-3 py-2 rounded-md text-sm font-medium transition-all
              ${
                activeCategory === cat.id
                  ? 'bg-white dark:bg-gray-700 shadow-sm text-gray-900 dark:text-white'
                  : 'text-gray-600 dark:text-gray-400 hover:text-gray-900 dark:hover:text-white'
              }
            `}
          >
            <div className="flex flex-col items-center gap-0.5">
              <span>{cat.label}</span>
              <span className="text-xs opacity-60">{cat.count}</span>
            </div>
          </button>
        ))}
      </div>

      {/* Operations Grid */}
      <div className="grid grid-cols-2 gap-3">
        {activeOps.map(op => {
          const needsInput = op.minValues > 0;
          return (
            <div key={op.operation} className="space-y-1.5">
              <div className="flex gap-2">
                <Button
                  onClick={() => handleOperation(op.operation, op.minValues, op.maxValues)}
                  disabled={disabled}
                  variant={op.category === 'basic' ? 'default' : 'secondary'}
                  className="flex-shrink-0 h-10 px-3"
                  title={op.description}
                >
                  <span className="text-lg mr-1.5">{op.symbol}</span>
                  <span className="text-xs">{op.label}</span>
                </Button>
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
                    className="flex-1 h-10 text-sm"
                  />
                )}
              </div>
              {errors[op.operation] && (
                <div className="text-xs text-red-600 dark:text-red-400 px-1">
                  {errors[op.operation]}
                </div>
              )}
            </div>
          );
        })}
      </div>

      {/* Helper Text */}
      <div className="text-xs text-gray-500 dark:text-gray-400 px-1">
        {mode === 'implicit' && 'Enter numeric values separated by commas'}
        {mode === 'explicit-literal' && 'Enter numeric strings separated by commas'}
        {mode === 'explicit-symbolic' && 'Use $current, $initial, or params.NAME'}
      </div>
    </div>
  );
}
