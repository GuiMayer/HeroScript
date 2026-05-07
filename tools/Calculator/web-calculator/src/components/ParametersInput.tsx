import { useState } from 'react';
import { Plus, X } from 'lucide-react';
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from './ui/Card';
import { Button } from './ui/Button';
import { Input } from './ui/Input';

interface ParametersInputProps {
  parameters: Record<string, number>;
  onChange: (params: Record<string, number>) => void;
  disabled?: boolean;
}

export function ParametersInput({ parameters, onChange, disabled }: ParametersInputProps) {
  const [newParamName, setNewParamName] = useState('');
  const [newParamValue, setNewParamValue] = useState('');
  const [error, setError] = useState<string | null>(null);

  const validateParamName = (name: string): boolean => {
    // Must be alphanumeric + underscore, cannot start with number
    const regex = /^[A-Za-z_][A-Za-z0-9_]*$/;
    return regex.test(name);
  };

  const addParameter = () => {
    setError(null);

    if (!newParamName.trim()) {
      setError('Parameter name is required');
      return;
    }

    if (!validateParamName(newParamName)) {
      setError('Invalid name. Use letters, numbers, underscore. Cannot start with number.');
      return;
    }

    if (parameters[newParamName] !== undefined) {
      setError('Parameter already exists');
      return;
    }

    const value = parseFloat(newParamValue);
    if (isNaN(value)) {
      setError('Invalid number value');
      return;
    }

    onChange({ ...parameters, [newParamName]: value });
    setNewParamName('');
    setNewParamValue('');
  };

  const removeParameter = (name: string) => {
    const newParams = { ...parameters };
    delete newParams[name];
    onChange(newParams);
  };

  const paramEntries = Object.entries(parameters);

  return (
    <Card>
      <CardHeader>
        <CardTitle>Parameters</CardTitle>
        <CardDescription>
          Define parameters for symbolic operands (params.NAME)
        </CardDescription>
      </CardHeader>
      <CardContent className="space-y-4">
        {/* Existing Parameters */}
        {paramEntries.length > 0 && (
          <div className="space-y-2">
            <div className="text-sm font-semibold text-gray-700 dark:text-gray-300">
              Current Parameters
            </div>
            <div className="space-y-2">
              {paramEntries.map(([name, value]) => (
                <div
                  key={name}
                  className="flex items-center gap-2 p-2 bg-gray-50 dark:bg-gray-800 rounded-lg"
                >
                  <code className="flex-1 text-sm font-mono text-purple-600 dark:text-purple-400">
                    params.{name}
                  </code>
                  <span className="text-sm font-semibold text-gray-900 dark:text-white">
                    = {value}
                  </span>
                  <Button
                    onClick={() => removeParameter(name)}
                    disabled={disabled}
                    variant="ghost"
                    size="sm"
                    className="h-6 w-6 p-0"
                  >
                    <X className="w-4 h-4" />
                  </Button>
                </div>
              ))}
            </div>
          </div>
        )}

        {/* Add New Parameter */}
        <div className="space-y-2">
          <div className="text-sm font-semibold text-gray-700 dark:text-gray-300">
            Add Parameter
          </div>
          <div className="flex gap-2">
            <Input
              type="text"
              placeholder="NAME"
              value={newParamName}
              onChange={(e) => {
                setNewParamName(e.target.value.toUpperCase());
                setError(null);
              }}
              disabled={disabled}
              className="flex-1 font-mono"
            />
            <Input
              type="number"
              placeholder="Value"
              value={newParamValue}
              onChange={(e) => {
                setNewParamValue(e.target.value);
                setError(null);
              }}
              disabled={disabled}
              className="flex-1"
            />
            <Button
              onClick={addParameter}
              disabled={disabled || !newParamName || !newParamValue}
              size="sm"
            >
              <Plus className="w-4 h-4" />
            </Button>
          </div>
          {error && (
            <div className="text-xs text-red-600 dark:text-red-400">
              {error}
            </div>
          )}
        </div>

        {/* Helper Text */}
        <div className="text-xs text-gray-500 dark:text-gray-400 space-y-1">
          <div>Use in operands: <code className="text-purple-600 dark:text-purple-400">params.NAME</code></div>
          <div>Special symbols: <code className="text-blue-600 dark:text-blue-400">$current</code>, <code className="text-green-600 dark:text-green-400">$initial</code></div>
        </div>
      </CardContent>
    </Card>
  );
}
