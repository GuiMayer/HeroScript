import { useState, useEffect } from 'react';
import { Calculator as CalculatorIcon, Loader2, AlertCircle } from 'lucide-react';
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from './components/ui/Card';
import { Button } from './components/ui/Button';
import { Input } from './components/ui/Input';
import { mathApi } from './services/mathApi';
import { StepHistory } from './components/StepHistory';
import { OperationButtons } from './components/OperationButtons';
import { ModeSelector } from './components/ModeSelector';
import { ParametersInput } from './components/ParametersInput';
import type { MathStepDto, MathExpressionResponse, ApiMode } from './types/api';

function App() {
  const [mode, setMode] = useState<ApiMode>('implicit');
  const [initialValue, setInitialValue] = useState<string>('0');
  const [parameters, setParameters] = useState<Record<string, number>>({});
  const [steps, setSteps] = useState<MathStepDto[]>([]);
  const [result, setResult] = useState<MathExpressionResponse | null>(null);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [apiConnected, setApiConnected] = useState<boolean | null>(null);

  // Check API connection on mount and poll every 2 seconds
  useEffect(() => {
    // Initial check
    mathApi.healthCheck().then(setApiConnected);

    // Poll every 2 seconds
    const intervalId = setInterval(() => {
      mathApi.healthCheck().then(setApiConnected);
    }, 2000);

    // Cleanup interval on unmount
    return () => clearInterval(intervalId);
  }, []);

  const handleModeChange = (newMode: ApiMode) => {
    // Clear steps when changing modes to avoid confusion
    if (steps.length > 0) {
      const confirmed = window.confirm(
        'Changing modes will clear all current steps. Continue?'
      );
      if (!confirmed) return;
    }
    
    setMode(newMode);
    setSteps([]);
    setError(null);
    
    // Clear parameters if switching away from symbolic mode
    if (newMode !== 'explicit-symbolic') {
      setParameters({});
    }
  };

  const addStep = (operation: string, values?: number[], operands?: string[]) => {
    // Validate that only one of values or operands is provided
    if (values && operands) {
      setError('Cannot have both values and operands in a step');
      return;
    }

    const newStep: MathStepDto = {
      operation,
      ...(values !== undefined && { values }),
      ...(operands !== undefined && { operands }),
    };

    setSteps([...steps, newStep]);
    setError(null);
  };

  const removeLastStep = () => {
    if (steps.length > 0) {
      setSteps(steps.slice(0, -1));
      setError(null);
    }
  };

  const clearAll = () => {
    setSteps([]);
    setResult(null);
    setError(null);
    setInitialValue('0');
  };

  const calculate = async () => {
    if (steps.length === 0) {
      setError('Add at least one operation before calculating');
      return;
    }

    setLoading(true);
    setError(null);

    try {
      const request: any = {
        initialValue: parseFloat(initialValue) || 0,
        steps,
      };

      // Add parameters if in symbolic mode and parameters exist
      if (mode === 'explicit-symbolic' && Object.keys(parameters).length > 0) {
        request.parameters = parameters;
      }

      const response = await mathApi.evaluate(request);
      setResult(response);
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to calculate');
      setResult(null);
    } finally {
      setLoading(false);
    }
  };

  const continueFromResult = () => {
    if (result) {
      setInitialValue(result.result.toString());
      setSteps([]);
      setResult(null);
    }
  };

  return (
    <div className="min-h-screen bg-gradient-to-br from-purple-50 to-blue-50 dark:from-gray-900 dark:to-gray-800 p-4">
      <div className="max-w-7xl mx-auto space-y-6">
        {/* Header */}
        <div className="text-center space-y-2 pt-8">
          <div className="flex items-center justify-center gap-3">
            <CalculatorIcon className="w-10 h-10 text-primary" />
            <h1 className="text-4xl font-bold text-gray-900 dark:text-white">
              HeroScript Calculator
            </h1>
          </div>
          <p className="text-gray-600 dark:text-gray-400">
            Testing MathExpression API with 15 operations and 3 modes
          </p>
          
          {/* API Status */}
          <div className="flex items-center justify-center gap-2 text-sm">
            <div
              className={`w-2 h-2 rounded-full ${
                apiConnected === null
                  ? 'bg-gray-400'
                  : apiConnected
                  ? 'bg-green-500'
                  : 'bg-red-500'
              }`}
            />
            <span className="text-gray-600 dark:text-gray-400">
              {apiConnected === null
                ? 'Checking API...'
                : apiConnected
                ? `API Connected (${mathApi.getBaseUrl()})`
                : 'API Disconnected - Start the API server'}
            </span>
          </div>
        </div>

        <div className="grid grid-cols-1 lg:grid-cols-2 gap-6">
          {/* Left Column - Calculator */}
          <div className="space-y-6">
            {/* Mode Selector */}
            <ModeSelector
              selectedMode={mode}
              onModeChange={handleModeChange}
              disabled={loading}
            />

            {/* Initial Value */}
            <Card>
              <CardHeader>
                <CardTitle>Initial Value</CardTitle>
                <CardDescription>
                  Starting value for the calculation
                </CardDescription>
              </CardHeader>
              <CardContent>
                <Input
                  type="number"
                  value={initialValue}
                  onChange={(e) => setInitialValue(e.target.value)}
                  placeholder="Enter initial value"
                  className="text-2xl font-mono text-center"
                  disabled={loading}
                />
              </CardContent>
            </Card>

            {/* Parameters (only for Mode 3) */}
            {mode === 'explicit-symbolic' && (
              <ParametersInput
                parameters={parameters}
                onChange={setParameters}
                disabled={loading}
              />
            )}

            {/* Operations */}
            <Card>
              <CardHeader>
                <CardTitle>Operations</CardTitle>
                <CardDescription>
                  Add operations to build your expression
                </CardDescription>
              </CardHeader>
              <CardContent>
                <OperationButtons
                  mode={mode}
                  parameters={parameters}
                  onAddStep={addStep}
                  disabled={loading}
                />
              </CardContent>
            </Card>

            {/* Actions */}
            <Card>
              <CardHeader>
                <CardTitle>Actions</CardTitle>
              </CardHeader>
              <CardContent className="space-y-3">
                <div className="grid grid-cols-2 gap-3">
                  <Button
                    onClick={calculate}
                    disabled={loading || steps.length === 0}
                    className="w-full"
                    size="lg"
                  >
                    {loading ? (
                      <>
                        <Loader2 className="w-4 h-4 mr-2 animate-spin" />
                        Calculating...
                      </>
                    ) : (
                      'Calculate'
                    )}
                  </Button>
                  <Button
                    onClick={clearAll}
                    variant="outline"
                    disabled={loading}
                    className="w-full"
                    size="lg"
                  >
                    Clear All
                  </Button>
                </div>
                <Button
                  onClick={removeLastStep}
                  variant="secondary"
                  disabled={loading || steps.length === 0}
                  className="w-full"
                >
                  Undo Last Step
                </Button>
                {result && (
                  <Button
                    onClick={continueFromResult}
                    variant="outline"
                    className="w-full"
                  >
                    Continue from Result
                  </Button>
                )}
              </CardContent>
            </Card>

            {/* Error Display */}
            {error && (
              <Card className="border-destructive">
                <CardContent className="pt-6">
                  <div className="flex items-start gap-3 text-destructive">
                    <AlertCircle className="w-5 h-5 mt-0.5 flex-shrink-0" />
                    <div>
                      <p className="font-semibold">Error</p>
                      <p className="text-sm">{error}</p>
                    </div>
                  </div>
                </CardContent>
              </Card>
            )}
          </div>

          {/* Right Column - History & Result */}
          <div className="space-y-6">
            <StepHistory
              steps={steps}
              result={result}
              initialValue={parseFloat(initialValue) || 0}
            />
          </div>
        </div>
      </div>
    </div>
  );
}

export default App;
