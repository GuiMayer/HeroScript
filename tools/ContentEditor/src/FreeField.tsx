import { useState } from 'react'
import { Plus, X } from 'lucide-react'
import { object } from './editor-state'
import type { RuleCopy } from './rule-copy'
import { TextField } from './TextField'

const defaults = { text: '', number: 0, boolean: false, object: {}, array: [], null: null }
type ValueKind = keyof typeof defaults

function valueKind(value: unknown): ValueKind | '' {
  if (value === undefined) return ''
  if (value === null) return 'null'
  if (Array.isArray(value)) return 'array'
  return typeof value === 'string' ? 'text' : typeof value as ValueKind
}

type Props = {
  label: string; value: unknown; update: (value: unknown) => void; copy: RuleCopy
  disabled: boolean; onTyping: (typing: boolean) => void; dictionary?: boolean
}

export function FreeField({ label, value, update, copy, disabled, onTyping, dictionary }: Props) {
  const kind = valueKind(value)
  const child = { copy, disabled, onTyping }
  const labels = { text: copy.textType, number: copy.numberType, boolean: copy.booleanType,
    object: copy.objectType, array: copy.arrayType, null: copy.nullType }
  if (!dictionary && kind !== 'object' && kind !== 'array') return <div className="free-field">
    <label className="field"><span>{label} · {copy.kind}</span>
      <select value={kind} disabled={disabled} onChange={(event) => {
        const next = event.target.value as ValueKind
        update(defaults[next])
      }}>
        <option value="">{copy.unset}</option>
        {Object.entries(labels).map(([type, name]) => <option key={type} value={type}>{name}</option>)}
      </select>
    </label>
    {(kind === 'text' || kind === 'number') && <TextField label={copy.value} value={String(value)} disabled={disabled}
      onTyping={onTyping} onCommit={(next) => {
        if (kind === 'text') update(next)
        else if (next.trim() && Number.isFinite(Number(next))) update(Number(next))
      }} />}
    {kind === 'boolean' && <label className="rule-check"><input type="checkbox" checked={value === true}
      disabled={disabled} onChange={(event) => update(event.target.checked)} /><span>{copy.value}</span></label>}
  </div>

  return <details className="component rule-fold">
    <summary>{label}</summary>
    {Array.isArray(value) && !dictionary ? <div className="rule-array">
      {value.map((item, index) => <div className="free-entry" key={index}>
        <FreeField {...child} label={`${copy.value} ${index + 1}`} value={item}
          update={(next) => update(value.map((current, position) => position === index ? next ?? null : current))} />
        <Remove copy={copy} disabled={disabled} remove={() => update(value.filter((_, position) => position !== index))} />
      </div>)}
      {!disabled && <button type="button" className="secondary" onClick={() => update([...value, null])}>
        <Plus size={15} />{copy.addItem}
      </button>}
    </div> : <Dictionary {...child} value={value} update={update} />}
    {!disabled && value !== undefined && <button type="button" className="text-button rule-array-remove"
      onClick={() => update(undefined)}>{copy.resetField}</button>}
  </details>
}

function Dictionary({ value, update, copy, disabled, onTyping }: Omit<Props, 'label'>) {
  const [key, setKey] = useState('')
  const entries = object(value)
  const newKey = key.trim()
  function add() {
    if (!newKey || Object.hasOwn(entries, newKey)) return
    update({ ...entries, [newKey]: null })
    setKey('')
  }
  return <div className="rule-array">
    {Object.entries(entries).map(([name, item]) => <div className="free-entry" key={name}>
      <FreeField label={name} value={item} copy={copy} disabled={disabled} onTyping={onTyping}
        update={(next) => update({ ...entries, [name]: next ?? null })} />
      <Remove copy={copy} disabled={disabled} remove={() => {
        const next = { ...entries }
        delete next[name]
        update(next)
      }} />
    </div>)}
    {!disabled && <div className="rule-reference-row">
      <label className="field"><span>{copy.propertyName}</span><input value={key} onChange={(event) => setKey(event.target.value)}
        onKeyDown={(event) => { if (event.key === 'Enter') { event.preventDefault(); add() } }} /></label>
      <button type="button" className="secondary" disabled={!newKey || Object.hasOwn(entries, newKey)} onClick={add}>
        <Plus size={15} />{copy.addItem}
      </button>
    </div>}
  </div>
}

function Remove({ copy, disabled, remove }: { copy: RuleCopy; disabled: boolean; remove: () => void }) {
  return !disabled && <button type="button" className="icon-button" aria-label={copy.removeItem}
    title={copy.removeItem} onClick={remove}><X size={15} /></button>
}
