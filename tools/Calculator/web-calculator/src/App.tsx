import { useState, useEffect } from 'react';
import { Calculator as CalculatorIcon, AlertCircle } from 'lucide-react';
import { mathApi } from './services/mathApi';
import { ModeSelectorTabs } from './components/ModeSelectorTabs';
import { InputArea } from './components/InputArea';
import { OperationsPanel } from './components/OperationsPanel';
import { OutputArea } from './components/OutputArea';
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
    mathApi.healthCheck().then(setApiConnected);
    const intervalId = setInterval(() => {
      mathApi.healthCheck().then(setApiConnected);
    }, 2000);
    return () => clearInterval(intervalId);
  }, []);

  // Keyboard shortcuts
  useEffect(() => {
    const handleKeyDown = (e: KeyboardEvent) => {
      // Ctrl+Enter: Calculate
      if (e.ctrlKey && e.key === 'Enter') {
        e.preventDefault();
        if (steps.length > 0 && !loading) {
          calculate();
        }
      }
      // Ctrl+Z: Undo
      if (e.ctrlKey && e.key === 'z') {
        e.preventDefault();
        removeLastStep();
      }
      // Ctrl+Shift+C: Clear all
      if (e.ctrlKey && e.shiftKey && e.key === 'C') {
        e.preventDefault();
        clearAll();
      }
    };

    window.addEventListener('keydown', handleKeyDown);
    return () => window.removeEventListener('keydown', handleKeyDown);
  }, [steps, loading]);

  const handleModeChange = (newMode: ApiMode) => {
    if (steps.length > 0) {
      const confirmed = window.confirm(
        'Changing modes will clear all current steps. Continue?'
      );
      if (!confirmed) return;
    }
    
    setMode(newMode);
    setSteps([]);
    setError(null);
    
    if (newMode !== 'explicit-symbolic') {
      setParameters({});
    }
  };

  const addStep = (operation: string, values?: number[], operands?: string[]) => {
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
      <div className="max-w-[1400px] mx-auto space-y-4">
        {/* Header */}
        <div className="text-center space-y-2 pt-6">
          <div className="flex items-center justify-center gap-3">
            <CalculatorIcon className="w-10 h-10 text-primary" />
            <h1 className="text-4xl font-bold text-gray-900 dark:text-white">
              HeroScript Calculator
            </h1>
          </div>
          <p className="text-gray-600 dark:text-gray-400 text-sm">
            Testing MathExpression API with 15 operations and 3 modes
          </p>
          
          {/* API Status */}
          <div className="flex items-center justify-center gap-2 text-xs">
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

        {/* Mode Selector */}
        <ModeSelectorTabs
          selectedMode={mode}
          onModeChange={handleModeChange}
          disabled={loading}
        />

        {/* Error Display */}
        {error && (
          <div className="bg-red-50 dark:bg-red-900/20 border border-red-200 dark:border-red-800 rounded-lg p-4">
            <div className="flex items-start gap-3 text-red-800 dark:text-red-200">
              <AlertCircle className="w-5 h-5 mt-0.5 flex-shrink-0" />
              <div>
                <p className="font-semibold text-sm">Error</p>
                <p className="text-sm">{error}</p>
              </div>
            </div>
          </div>
        )}

        {/* 3-Column Layout */}
        <div className="grid grid-cols-1 lg:grid-cols-[300px_1fr_350px] gap-4">
          {/* Column 1: Input Area */}
          <div className="bg-white dark:bg-gray-800 rounded-lg border border-gray-200 dark:border-gray-700 p-4">
            <InputArea
              mode={mode}
              initialValue={initialValue}
              onInitialValueChange={setInitialValue}
              parameters={parameters}
              onParametersChange={setParameters}
              onClearAll={clearAll}
              onUndo={removeLastStep}
              canUndo={steps.length > 0}
              disabled={loading}
            />
          </div>

          {/* Column 2: Operations Panel */}
          <div className="bg-white dark:bg-gray-800 rounded-lg border border-gray-200 dark:border-gray-700 p-4">
            <h2 className="text-lg font-semibold mb-4">Operations</h2>
            <OperationsPanel
              mode={mode}
              parameters={parameters}
              onAddStep={addStep}
              disabled={loading}
            />
          </div>

          {/* Column 3: Output Area */}
          <div className="bg-white dark:bg-gray-800 rounded-lg border border-gray-200 dark:border-gray-700 p-4">
            <OutputArea
              steps={steps}
              result={result}
              initialValue={parseFloat(initialValue) || 0}
              loading={loading}
              onCalculate={calculate}
              onContinueFromResult={continueFromResult}
              disabled={loading}
            />
          </div>
        </div>

        {/* Keyboard Shortcuts Hint */}
        <div className="text-center text-xs text-gray-500 dark:text-gray-400 pb-4">
          <kbd className="px-2 py-1 bg-gray-100 dark:bg-gray-700 rounded">Ctrl+Enter</kbd> Calculate
          {' • '}
          <kbd className="px-2 py-1 bg-gray-100 dark:bg-gray-700 rounded">Ctrl+Z</kbd> Undo
          {' • '}
          <kbd className="px-2 py-1 bg-gray-100 dark:bg-gray-700 rounded">Ctrl+Shift+C</kbd> Clear
        </div>
      </div>
    </div>
  );
}

export default App;
