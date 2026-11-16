import { useEffect, useState, type ReactNode } from 'react'
import { Braces, Pencil } from 'lucide-react'
import type { Definition } from './api/client'
import type { CardCopy } from './card-copy'
import { object, propertyKey } from './editor-state'
import { parseAuthoringJson, stringifyAuthoringJson } from './authoring-json'

type Props = {
  value: Definition; disabled: boolean; copy: CardCopy; label: string; invalidMessage: string
  identity?: string; id?: unknown; update: (value: Definition) => void; children?: ReactNode
}

export function JsonSource({ value, disabled, copy, label, invalidMessage, identity, id, update, children }: Props) {
  const serialized = stringifyAuthoringJson(value, 2)
  const [text, setText] = useState(serialized)
  const [editing, setEditing] = useState(false)
  const [invalid, setInvalid] = useState(false)
  useEffect(() => { setText(serialized); setInvalid(false) }, [serialized])
  const editable = editing && !disabled

  function toggle() {
    if (editing) {
      setText(serialized)
      setInvalid(false)
    }
    setEditing(!editing)
  }

  function apply() {
    try {
      const input = parseAuthoringJson(text)
      const parsed = object(input)
      if (parsed !== input || (identity && parsed[propertyKey(parsed, identity)] !== id)) throw new Error()
      update(parsed)
      setInvalid(false)
    } catch { setInvalid(true) }
  }

  return <section className="form-section">
    <div className="json-heading">
      <h3>{label}</h3>
      {!disabled && <button type="button" className="icon-button" aria-pressed={editing}
        aria-label={copy.editJson} title={editing ? copy.stopEditingJson : copy.editJson} onClick={toggle}><Pencil size={17} /></button>}
    </div>
    {children}
    <textarea className="json-input" aria-label={label} spellCheck={false} readOnly={!editable}
      value={editable ? text : serialized} onChange={(event) => { setText(event.target.value); setInvalid(false) }} />
    {editable && invalid && <p className="error" role="alert">{invalidMessage}</p>}
    {editable && <button type="button" className="secondary" disabled={text === serialized} onClick={apply}>
      <Braces size={15} />{copy.applyJson}
    </button>}
  </section>
}
