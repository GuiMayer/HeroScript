import type { ApiMode } from '../types/api';

interface ModeSelectorTabsProps {
  selectedMode: ApiMode;
  onModeChange: (mode: ApiMode) => void;
  disabled?: boolean;
}

const modes = [
  {
    id: 'implicit' as ApiMode,
    label: 'Mode 1: Implicit',
    shortLabel: 'Implicit',
    description: 'Simple values with implicit accumulator',
    badge: 'M1',
  },
  {
    id: 'explicit-literal' as ApiMode,
    label: 'Mode 2: Literal',
    shortLabel: 'Literal',
    description: 'Numeric operands as strings',
    badge: 'M2',
  },
  {
    id: 'explicit-symbolic' as ApiMode,
    label: 'Mode 3: Symbolic',
    shortLabel: 'Symbolic',
    description: 'Dynamic operands with $current, $initial, params.X',
    badge: 'M3',
  },
];

export function ModeSelectorTabs({ selectedMode, onModeChange, disabled }: ModeSelectorTabsProps) {
  return (
    <div className="bg-card rounded-lg border border-border p-1">
      <div className="flex gap-1">
        {modes.map((mode) => {
          const isActive = selectedMode === mode.id;
          return (
            <button
              key={mode.id}
              onClick={() => !disabled && onModeChange(mode.id)}
              disabled={disabled}
              className={`
                flex-1 px-4 py-3 rounded-md font-medium text-sm transition-all
                ${
                  isActive
                    ? 'bg-primary text-primary-foreground shadow-sm'
                    : 'text-muted-foreground hover:bg-muted'
                }
                ${disabled ? 'opacity-50 cursor-not-allowed' : 'cursor-pointer'}
              `}
              title={mode.description}
            >
              <div className="flex flex-col items-center gap-1">
                <div className="flex items-center gap-2">
                  <span className={`
                    text-xs font-mono px-1.5 py-0.5 rounded
                    ${isActive ? 'bg-primary-foreground/20' : 'bg-muted'}
                  `}>
                    {mode.badge}
                  </span>
                  <span className="hidden sm:inline">{mode.shortLabel}</span>
                  <span className="sm:hidden">{mode.badge}</span>
                </div>
                <span className="text-xs opacity-75 hidden md:block">{mode.description}</span>
              </div>
            </button>
          );
        })}
      </div>
    </div>
  );
}
