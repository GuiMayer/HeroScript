import type { ApiMode } from '../types/api';
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from './ui/Card';

interface ModeSelectorProps {
  selectedMode: ApiMode;
  onModeChange: (mode: ApiMode) => void;
  disabled?: boolean;
}

export function ModeSelector({ selectedMode, onModeChange, disabled }: ModeSelectorProps) {
  const modes: { value: ApiMode; label: string; description: string }[] = [
    {
      value: 'implicit',
      label: 'Mode 1: Implicit (Values)',
      description: 'Simple mode - values modify accumulator implicitly',
    },
    {
      value: 'explicit-literal',
      label: 'Mode 2: Explicit Literal',
      description: 'Explicit numeric operands as strings',
    },
    {
      value: 'explicit-symbolic',
      label: 'Mode 3: Symbolic',
      description: 'Dynamic operands with $current, $initial, params.X',
    },
  ];

  return (
    <Card>
      <CardHeader>
        <CardTitle>API Mode</CardTitle>
        <CardDescription>
          Choose how operations are evaluated
        </CardDescription>
      </CardHeader>
      <CardContent>
        <div className="space-y-3">
          {modes.map((mode) => (
            <label
              key={mode.value}
              className={`flex items-start gap-3 p-3 rounded-lg border-2 cursor-pointer transition-colors ${
                selectedMode === mode.value
                  ? 'border-primary bg-primary/5'
                  : 'border-gray-200 dark:border-gray-700 hover:border-gray-300 dark:hover:border-gray-600'
              } ${disabled ? 'opacity-50 cursor-not-allowed' : ''}`}
            >
              <input
                type="radio"
                name="mode"
                value={mode.value}
                checked={selectedMode === mode.value}
                onChange={(e) => onModeChange(e.target.value as ApiMode)}
                disabled={disabled}
                className="mt-1"
              />
              <div className="flex-1">
                <div className="font-semibold text-sm text-gray-900 dark:text-white">
                  {mode.label}
                </div>
                <div className="text-xs text-gray-600 dark:text-gray-400 mt-1">
                  {mode.description}
                </div>
              </div>
            </label>
          ))}
        </div>
      </CardContent>
    </Card>
  );
}
