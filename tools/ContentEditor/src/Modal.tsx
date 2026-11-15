import { useEffect, useId, useRef, type ReactNode } from 'react'
import { X } from 'lucide-react'

export function Modal({ title, closeLabel, onClose, children }: {
  title: string; closeLabel: string; onClose: () => void; children: ReactNode
}) {
  const dialog = useRef<HTMLDialogElement>(null)
  const id = useId()
  useEffect(() => {
    const element = dialog.current
    element?.showModal()
    return () => element?.close()
  }, [])
  return <dialog ref={dialog} className="preferences-dialog" aria-labelledby={id} onCancel={(event) => {
    event.preventDefault()
    onClose()
  }} onClick={(event) => { if (event.target === event.currentTarget) onClose() }}>
    <div className="dialog-title"><h2 id={id}>{title}</h2>
      <button className="icon-button" type="button" aria-label={closeLabel} onClick={onClose}><X size={18} /></button></div>
    {children}
  </dialog>
}
