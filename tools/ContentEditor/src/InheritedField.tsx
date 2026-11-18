import type { ReactNode } from 'react'
import { RotateCcw } from 'lucide-react'
import type { CardCopy } from './card-copy'
import { same } from './editor-state'

export interface InheritedValue {
  value: unknown
  copy: CardCopy
  restoringDisabled: boolean
}

export function InheritedField({ inherited, value, label, restore, children }: {
  inherited?: InheritedValue
  value: unknown
  label: string
  restore: () => void
  children: ReactNode
}) {
  if (!inherited) return children
  const changed = !same(value, inherited.value)
  return <div className={changed ? 'customized-field' : undefined}>
    {children}
    {changed && <div className="customized-field-actions">
      <span className="customization-label">{inherited.copy.customized}</span>
      <button type="button" className="text-button" disabled={inherited.restoringDisabled}
        aria-label={`${inherited.copy.restoreInherited}: ${label}`} onClick={restore}>
        <RotateCcw size={13} />{inherited.copy.restoreInherited}
      </button>
    </div>}
  </div>
}
