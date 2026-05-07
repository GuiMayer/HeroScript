import { useState } from 'react';
import { ChevronDown, ChevronRight } from 'lucide-react';
import { Input } from './ui/Input';
import { Button } from './ui/Button';
import type { ApiMode } from '../types/api';

interface InputAreaProps {
  mode: ApiMode;
  initialValue: string;
  onInitialValueChange: (value: string) => void;
  parameters: Record<string, number>;
  onParametersChange: (params: Record<string, number>) => void;
  onClearAll: () => void;
  onUndo: () => void;
  canUndo: boolean;
  disabled?: boolean;
}

export function InputArea({
  mode,
  initialValue,
  onInitialValueChange,
  parameters,
  onParametersChange,
  onClearAll,
  onUndo,
  canUndo,
  disabled,
}: InputAreaProps) {
  const [paramsExpanded, setParamsExpanded] = useState(mode === 'explicit-symbolic');
  const [newParamName, setNewParamName] = useState('');
  const [newParamValue, setNewParamValue] = useState('');

  // Auto-expand parameters when switching to symbolic mode
  useState(() => {
    if (mode === 'explicit-symbolic' && !paramsExpanded) {
      setParamsExpanded(true);
    }
  });

  const handleAddParameter = () => {
    if (!newParamName.trim() || newParamValue.trim() === '') return;
    
    const name = newParamName.trim();
    const value = parseFloat(newParamValue);
    
    if (isNaN(value)) return;
    
    onParametersChange({ ...parameters, [name]: value });
    setNewParamName('');
    setNewParamValue('');
  };

  const handleRemoveParameter = (name: string) => {
    const newParams = { ...parameters };
    delete newParams[name];
    onParametersChange(newParams);
  };

  const paramCount = Object.keys(parameters).length;

  return (
    <div className="space-y-4">
      {/* Initial Value */}
      <div>
        <label className="block text-sm font-medium text-foreground mb-2">
          Starting Value
        </label>
        <Input
          type="number"
          value={initialValue}
          onChange={(e) => onInitialValueChange(e.target.value)}
          placeholder="0"
          className="text-4xl font-mono text-center h-20 font-bold"
          disabled={disabled}
        />
      </div>

      {/* Parameters Section (Mode 3 only) */}
      {mode === 'explicit-symbolic' && (
        <div className="border border-border rounded-lg overflow-hidden">
          <button
            onClick={() => setParamsExpanded(!paramsExpanded)}
            className="w-full flex items-center justify-between p-3 bg-muted hover:bg-muted/80 transition-colors"
            disabled={disabled}
          >
            <div className="flex items-center gap-2">
              {paramsExpanded ? (
                <ChevronDown className="w-4 h-4" />
              ) : (
                <ChevronRight className="w-4 h-4" />
              )}
              <span className="font-medium text-sm">Parameters</span>
              {paramCount > 0 && (
                <span className="text-xs px-2 py-0.5 rounded-full bg-primary/10 text-primary font-mono">
                  {paramCount}
                </span>
              )}
            </div>
          </button>

          {paramsExpanded && (
            <div className="p-4 space-y-3">
              {/* Existing Parameters */}
              {paramCount > 0 && (
                <div className="space-y-2 mb-3">
                  {Object.entries(parameters).map(([name, value]) => (
                  <div
                    key={name}
                    className="flex items-center gap-2 p-2 bg-muted rounded-lg"
                  >
                    <code className="flex-1 text-sm font-mono text-purple-600 dark:text-purple-400">
                      params.{name}
                    </code>
                    <span className="text-sm font-mono text-muted-foreground">
                      = {value}
                    </span>
                      <Button
                        onClick={() => handleRemoveParameter(name)}
                        variant="ghost"
                        size="sm"
                        disabled={disabled}
                        className="h-6 w-6 p-0"
                      >
                        ×
                      </Button>
                    </div>
                  ))}
                </div>
              )}

              {/* Add New Parameter */}
              <div className="space-y-2">
                <div className="flex gap-2">
                  <Input
                    type="text"
                    placeholder="Name (e.g., MULTIPLIER)"
                    value={newParamName}
                    onChange={(e) => setNewParamName(e.target.value)}
                    disabled={disabled}
                    className="flex-1 font-mono text-sm"
                  />
                  <Input
                    type="number"
                    placeholder="Value"
                    value={newParamValue}
                    onChange={(e) => setNewParamValue(e.target.value)}
                    disabled={disabled}
                    className="w-24 font-mono text-sm"
                  />
                </div>
                <Button
                  onClick={handleAddParameter}
                  variant="outline"
                  size="sm"
                  disabled={disabled || !newParamName.trim() || !newParamValue.trim()}
                  className="w-full"
                >
                  Add Parameter
                </Button>
              </div>

              <div className="text-xs text-muted-foreground space-y-1">
                <p>Use parameters in operations:</p>
                <code className="block text-purple-600 dark:text-purple-400">params.{newParamName || 'NAME'}</code>
              </div>
            </div>
          )}
        </div>
      )}

      {/* Quick Actions */}
      <div className="flex gap-2 pt-2">
        <Button
          onClick={onUndo}
          variant="ghost"
          size="sm"
          disabled={disabled || !canUndo}
          className="flex-1"
        >
          Undo Last
        </Button>
        <Button
          onClick={onClearAll}
          variant="ghost"
          size="sm"
          disabled={disabled}
          className="flex-1"
        >
          Clear All
        </Button>
      </div>
    </div>
  );
}
