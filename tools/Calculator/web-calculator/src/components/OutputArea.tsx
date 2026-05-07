import { Loader2, Play, TrendingUp, Clock, Zap } from 'lucide-react';
import { Button } from './ui/Button';
import type { MathStepDto, MathExpressionResponse } from '../types/api';
import { OPERATIONS } from '../types/api';

interface OutputAreaProps {
  steps: MathStepDto[];
  result: MathExpressionResponse | null;
  initialValue: number;
  loading: boolean;
  onCalculate: () => void;
  onContinueFromResult: () => void;
  disabled?: boolean;
}

export function OutputArea({
  steps,
  result,
  initialValue,
  loading,
  onCalculate,
  onContinueFromResult,
  disabled,
}: OutputAreaProps) {
  const getOperationMetadata = (operation: string) => {
    return OPERATIONS.find(op => op.operation === operation);
  };

  const formatValue = (value: number) => {
    return Number.isInteger(value) ? value.toString() : value.toFixed(4);
  };

  const formatValues = (values?: number[]) => {
    if (!values || values.length === 0) return '';
    if (values.length === 1) return formatValue(values[0]);
    return values.map(formatValue).join(', ');
  };

  const formatOperand = (operand: string) => {
    if (operand === '$current') {
      return <span className="text-blue-600 dark:text-blue-400 font-semibold">{operand}</span>;
    }
    if (operand === '$initial') {
      return <span className="text-green-600 dark:text-green-400 font-semibold">{operand}</span>;
    }
    if (operand.startsWith('params.')) {
      return <span className="text-purple-600 dark:text-purple-400 font-semibold">{operand}</span>;
    }
    return <span className="text-gray-700 dark:text-gray-300">{operand}</span>;
  };

  const formatOperands = (operands?: string[]) => {
    if (!operands || operands.length === 0) return null;
    return (
      <span className="inline-flex flex-wrap gap-1 items-center">
        {operands.map((operand, idx) => (
          <span key={idx} className="inline-flex items-center">
            {formatOperand(operand)}
            {idx < operands.length - 1 && <span className="text-gray-400">,</span>}
          </span>
        ))}
      </span>
    );
  };

  return (
    <div className="space-y-4">
      {/* Calculate Button - Prominent */}
      <Button
        onClick={onCalculate}
        disabled={disabled || loading || steps.length === 0}
        className="w-full h-14 text-lg font-semibold"
        size="lg"
      >
        {loading ? (
          <>
            <Loader2 className="w-5 h-5 mr-2 animate-spin" />
            Calculating...
          </>
        ) : (
          <>
            <Play className="w-5 h-5 mr-2" />
            Calculate
          </>
        )}
      </Button>

      {/* Result Display */}
      {result && (
        <div className="space-y-4">
          <div className="bg-gradient-to-br from-primary/5 to-primary/10 dark:from-primary/10 dark:to-primary/20 rounded-lg border-2 border-primary/50 p-6">
            <div className="flex items-center gap-2 mb-3">
              <TrendingUp className="w-5 h-5 text-primary" />
              <span className="text-sm font-medium text-gray-600 dark:text-gray-400">Result</span>
            </div>
            <div className="text-6xl font-bold font-mono text-primary text-center">
              {formatValue(result.result)}
            </div>
          </div>

          {/* Metadata */}
          <div className="grid grid-cols-2 gap-3">
            <div className="flex items-center gap-2 p-3 bg-gray-50 dark:bg-gray-800 rounded-lg">
              <Clock className="w-4 h-4 text-gray-500" />
              <div>
                <div className="text-xs text-gray-500">Time</div>
                <div className="font-semibold font-mono text-sm">
                  {result.executionTimeMs.toFixed(2)} ms
                </div>
              </div>
            </div>
            <div className="flex items-center gap-2 p-3 bg-gray-50 dark:bg-gray-800 rounded-lg">
              <Zap className="w-4 h-4 text-gray-500" />
              <div>
                <div className="text-xs text-gray-500">Steps</div>
                <div className="font-semibold font-mono text-sm">
                  {result.steps.length}
                </div>
              </div>
            </div>
          </div>

          {/* Continue Button */}
          <Button
            onClick={onContinueFromResult}
            variant="outline"
            className="w-full"
          >
            Continue from Result
          </Button>
        </div>
      )}

      {/* Timeline */}
      <div className="border border-gray-200 dark:border-gray-700 rounded-lg overflow-hidden">
        <div className="bg-gray-50 dark:bg-gray-800 px-4 py-2 border-b border-gray-200 dark:border-gray-700">
          <div className="flex items-center justify-between">
            <span className="text-sm font-medium">Timeline</span>
            <span className="text-xs text-gray-500">
              {steps.length} step{steps.length !== 1 ? 's' : ''}
            </span>
          </div>
        </div>

        <div className="max-h-[400px] overflow-y-auto">
          {steps.length === 0 ? (
            <div className="text-center py-12 text-gray-500 dark:text-gray-400">
              <p className="text-sm">No operations yet</p>
              <p className="text-xs mt-1">Add operations to build your expression</p>
            </div>
          ) : (
            <div className="p-3 space-y-2">
              {/* Initial Value */}
              <div className="flex items-center gap-2 text-sm">
                <div className="w-6 h-6 rounded-full bg-gray-200 dark:bg-gray-700 flex items-center justify-center text-xs font-semibold">
                  0
                </div>
                <span className="font-mono font-semibold">{formatValue(initialValue)}</span>
                <span className="text-xs text-gray-500">initial</span>
              </div>

              {/* Steps */}
              {steps.map((step, index) => {
                const metadata = getOperationMetadata(step.operation);
                return (
                  <div
                    key={index}
                    className="flex items-center gap-2 text-sm pl-2 border-l-2 border-gray-200 dark:border-gray-700 ml-3"
                  >
                    <div className="w-6 h-6 rounded-full bg-primary/10 flex items-center justify-center text-xs font-semibold text-primary">
                      {index + 1}
                    </div>
                    <span className="font-semibold">{metadata?.symbol || step.operation}</span>
                    {step.values && step.values.length > 0 && (
                      <span className="text-xs font-mono text-gray-600 dark:text-gray-400">
                        {formatValues(step.values)}
                      </span>
                    )}
                    {step.operands && step.operands.length > 0 && (
                      <span className="text-xs font-mono">
                        {formatOperands(step.operands)}
                      </span>
                    )}
                  </div>
                );
              })}
            </div>
          )}
        </div>
      </div>
    </div>
  );
}
