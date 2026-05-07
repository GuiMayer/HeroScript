import { Clock, TrendingUp, Zap } from 'lucide-react';
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from './ui/Card';
import type { MathStepDto, MathExpressionResponse } from '../types/api';
import { OPERATIONS } from '../types/api';

interface StepHistoryProps {
  steps: MathStepDto[];
  result: MathExpressionResponse | null;
  initialValue: number;
}

export function StepHistory({ steps, result, initialValue }: StepHistoryProps) {
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
      <div className="flex flex-wrap gap-1 items-center">
        {operands.map((operand, idx) => (
          <span key={idx} className="inline-flex items-center">
            {formatOperand(operand)}
            {idx < operands.length - 1 && <span className="text-gray-400 mx-1">,</span>}
          </span>
        ))}
      </div>
    );
  };

  const getStepMode = (step: MathStepDto): 'M1' | 'M2' | 'M3' | null => {
    if (step.values && step.values.length >= 0) return 'M1';
    if (step.operands) {
      // Check if any operand is symbolic
      const hasSymbolic = step.operands.some(op => 
        op === '$current' || op === '$initial' || op.startsWith('params.')
      );
      return hasSymbolic ? 'M3' : 'M2';
    }
    return null;
  };

  return (
    <div className="space-y-6">
      {/* Current Steps */}
      <Card>
        <CardHeader>
          <CardTitle>Expression Steps</CardTitle>
          <CardDescription>
            {steps.length === 0
              ? 'No operations added yet'
              : `${steps.length} operation${steps.length !== 1 ? 's' : ''} queued`}
          </CardDescription>
        </CardHeader>
        <CardContent>
          {steps.length === 0 ? (
            <div className="text-center py-8 text-muted-foreground">
              <p>Add operations to build your expression</p>
            </div>
          ) : (
            <div className="space-y-3">
              {/* Initial Value */}
              <div className="flex items-center gap-3 p-3 bg-muted/50 rounded-lg">
                <div className="w-8 h-8 rounded-full bg-primary/10 flex items-center justify-center text-primary font-semibold text-sm">
                  0
                </div>
                <div className="flex-1">
                  <div className="font-semibold text-lg font-mono">
                    {formatValue(initialValue)}
                  </div>
                  <div className="text-xs text-muted-foreground">Initial Value</div>
                </div>
              </div>

              {/* Timeline connector */}
              <div className="ml-4 border-l-2 border-dashed border-muted-foreground/30 pl-6 space-y-3">
                {steps.map((step, index) => {
                  const metadata = getOperationMetadata(step.operation);
                  const mode = getStepMode(step);
                  return (
                    <div
                      key={index}
                      className="relative flex items-center gap-3 p-3 bg-background border rounded-lg hover:border-primary/50 transition-colors"
                    >
                      {/* Timeline dot */}
                      <div className="absolute -left-[29px] w-4 h-4 rounded-full bg-primary border-4 border-background" />
                      
                      <div className="w-8 h-8 rounded-full bg-secondary flex items-center justify-center text-secondary-foreground font-semibold text-sm">
                        {index + 1}
                      </div>
                      <div className="flex-1">
                        <div className="flex items-center gap-2 flex-wrap">
                          <span className="font-semibold text-lg">
                            {metadata?.symbol || step.operation}
                          </span>
                          <span className="text-sm text-muted-foreground">
                            {metadata?.label || step.operation}
                          </span>
                          {mode && (
                            <span className="text-xs px-1.5 py-0.5 rounded bg-primary/10 text-primary font-mono">
                              {mode}
                            </span>
                          )}
                        </div>
                        {step.values && step.values.length > 0 && (
                          <div className="text-sm font-mono text-muted-foreground mt-1">
                            {formatValues(step.values)}
                          </div>
                        )}
                        {step.operands && step.operands.length > 0 && (
                          <div className="text-sm font-mono mt-1">
                            {formatOperands(step.operands)}
                          </div>
                        )}
                      </div>
                    </div>
                  );
                })}
              </div>
            </div>
          )}
        </CardContent>
      </Card>

      {/* Result Display */}
      {result && (
        <Card className="border-primary/50 bg-primary/5">
          <CardHeader>
            <CardTitle className="flex items-center gap-2">
              <TrendingUp className="w-5 h-5 text-primary" />
              Result
            </CardTitle>
          </CardHeader>
          <CardContent className="space-y-4">
            {/* Final Result */}
            <div className="text-center p-6 bg-background rounded-lg border-2 border-primary">
              <div className="text-sm text-muted-foreground mb-2">Final Result</div>
              <div className="text-5xl font-bold font-mono text-primary">
                {formatValue(result.result)}
              </div>
            </div>

            {/* Metadata */}
            <div className="grid grid-cols-2 gap-3">
              <div className="flex items-center gap-2 p-3 bg-background rounded-lg">
                <Clock className="w-4 h-4 text-muted-foreground" />
                <div>
                  <div className="text-xs text-muted-foreground">Execution Time</div>
                  <div className="font-semibold font-mono">
                    {result.executionTimeMs.toFixed(2)} ms
                  </div>
                </div>
              </div>
              <div className="flex items-center gap-2 p-3 bg-background rounded-lg">
                <Zap className="w-4 h-4 text-muted-foreground" />
                <div>
                  <div className="text-xs text-muted-foreground">Operations</div>
                  <div className="font-semibold font-mono">
                    {result.steps.length}
                  </div>
                </div>
              </div>
            </div>

            {/* Expression Summary */}
            <div className="p-3 bg-background rounded-lg">
              <div className="text-xs text-muted-foreground mb-2">Expression</div>
              <div className="font-mono text-sm break-all">
                {formatValue(result.initialValue)}
                {result.steps.map((step, index) => {
                  const metadata = getOperationMetadata(step.operation);
                  const symbol = metadata?.symbol || step.operation;
                  const values = formatValues(step.values);
                  return (
                    <span key={index}>
                      {' → '}
                      <span className="text-primary font-semibold">{symbol}</span>
                      {values && `(${values})`}
                    </span>
                  );
                })}
                {' = '}
                <span className="text-primary font-bold">{formatValue(result.result)}</span>
              </div>
            </div>
          </CardContent>
        </Card>
      )}
    </div>
  );
}
