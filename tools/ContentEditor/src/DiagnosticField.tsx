import { useEffect, useRef, type ReactNode } from 'react'
import type { DiagnosticFocus } from './diagnostic-target'

export function DiagnosticField({ focus, children }: { focus?: DiagnosticFocus; children: ReactNode }) {
  const container = useRef<HTMLDivElement>(null)
  useEffect(() => {
    if (!focus || !container.current) return
    const element = container.current
    element.scrollIntoView({ block: 'center' })
    const control = element.querySelector<HTMLElement>('input:not([disabled]), select:not([disabled]), textarea:not([disabled]), summary') ?? element
    control.focus({ preventScroll: true })
  }, [focus])
  if (!focus) return children
  return <div ref={container} className="diagnostic-field" tabIndex={-1}>{children}</div>
}
