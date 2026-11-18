import { useState, type ReactNode } from 'react'
import { Pencil, Plus, X } from 'lucide-react'
import type { Definition } from './api/client'
import { object } from './editor-state'
import type { RuleCopy } from './rule-copy'
import { TextField } from './TextField'

const defaults = { text: '', number: 0, boolean: false, object: {}, array: [], null: null }
type ValueKind = keyof typeof defaults
const valueKinds = Object.keys(defaults) as ValueKind[]

function valueKind(value: unknown): ValueKind | '' {
  if (value === undefined) return ''
  if (value === null) return 'null'
  if (Array.isArray(value)) return 'array'
  if (typeof value === 'bigint') return 'number'
  return typeof value === 'string' ? 'text' : typeof value as ValueKind
}

type Props = {
  label: string; value: unknown; update: (value: unknown) => void; copy: RuleCopy
  disabled: boolean; onTyping: (typing: boolean) => void; dictionary?: boolean
}

function ValueType({ label, kind, copy, disabled, select, allowUnset = false }: {
  label: string; kind: ValueKind | ''; copy: RuleCopy; disabled: boolean
  select: (kind: ValueKind | '') => void; allowUnset?: boolean
}) {
  const labels = { text: copy.textType, number: copy.numberType, boolean: copy.booleanType,
    object: copy.objectType, array: copy.arrayType, null: copy.nullType }
  return <label className="field"><span>{label}</span>
    <select value={kind} disabled={disabled} onChange={(event) => select(event.target.value as ValueKind | '')}>
      {(allowUnset || kind === '') && <option value="" disabled={!allowUnset}>{copy.unset}</option>}
      {valueKinds.map((type) => <option key={type} value={type}>{labels[type]}</option>)}
    </select>
  </label>
}

export function FreeField({ label, value, update, copy, disabled, onTyping, dictionary }: Props) {
  const kind = valueKind(value)
  const child = { copy, disabled, onTyping }
  const valueLabel = `${label} · ${copy.value}`
  const type = <ValueType label={`${label} · ${copy.valueType}`} kind={kind} copy={copy} disabled={disabled}
    allowUnset={value === undefined} select={(next) => update(next ? defaults[next] : undefined)} />
  if (!dictionary && kind !== 'object' && kind !== 'array') return <div className="free-field">
    {type}
    {(kind === 'text' || kind === 'number') && <TextField label={valueLabel} value={String(value)} disabled={disabled}
      onTyping={onTyping} onCommit={(next) => {
        if (kind === 'text') update(next)
        else if (typeof value === 'bigint' && /^-?\d+$/.test(next.trim())) update(BigInt(next.trim()))
        else if (next.trim() && Number.isFinite(Number(next))) update(Number(next))
      }} />}
    {kind === 'boolean' && <label className="rule-check"><input type="checkbox" checked={value === true}
      disabled={disabled} onChange={(event) => update(event.target.checked)} /><span>{valueLabel}</span></label>}
  </div>

  return <details className="component rule-fold">
    <summary>{label}</summary>
    {!dictionary && type}
    {Array.isArray(value) && !dictionary ? <div className="rule-array">
      {value.map((item, index) => <div className="free-entry" key={index}>
        <FreeField {...child} label={`${copy.value} ${index + 1}`} value={item}
          update={(next) => update(value.map((current, position) => position === index ? next ?? null : current))} />
        <Remove copy={copy} disabled={disabled} remove={() => update(value.filter((_, position) => position !== index))} />
      </div>)}
      {!disabled && <button type="button" className="secondary" onClick={() => update([...value, null])}>
        <Plus size={15} />{copy.addItem}
      </button>}
    </div> : <PropertyFields {...child} value={object(value)} update={update} />}
    {dictionary && !disabled && value !== undefined && <button type="button" className="text-button rule-array-remove"
      onClick={() => update(undefined)}>{copy.resetField}</button>}
  </details>
}

export function PropertyFields({ value: entries, update, copy, disabled, onTyping, typed, keyInput }: {
  value: Definition; update: (value: Definition) => void; copy: RuleCopy; disabled: boolean
  onTyping: (typing: boolean) => void
  typed?: { initial: unknown; render: (name: string, value: unknown, update: (next: unknown) => void,
    restore: (next: unknown) => void) => ReactNode }
  keyInput?: (value: string, change: (value: string) => void) => ReactNode
}) {
  const [key, setKey] = useState('')
  const [kind, setKind] = useState<ValueKind>('text')
  const [renaming, setRenaming] = useState<string | null>(null)
  const [renameKey, setRenameKey] = useState('')
  const newKey = key.trim()
  const canRename = renaming !== null && !!renameKey.trim() && renameKey.trim() !== renaming && !Object.hasOwn(entries, renameKey.trim())
  function add() {
    if (!newKey || Object.hasOwn(entries, newKey)) return
    update({ ...entries, [newKey]: typed ? typed.initial : defaults[kind] })
    setKey('')
  }
  function rename() {
    if (!canRename) return
    update(Object.fromEntries(Object.entries(entries).map(([name, item]) => [name === renaming ? renameKey.trim() : name, item])))
    setRenaming(null)
  }
  return <div className="rule-array">
    {Object.entries(entries).map(([name, item]) => {
      const change = (next: unknown) => update({ ...entries, [name]: next ?? (typed ? typed.initial : null) })
      return <div className="free-entry" key={name}>
        {typed ? typed.render(name, item, change, (baseline) => {
          const next = { ...entries, [name]: baseline }
          if (baseline === undefined) delete next[name]
          update(next)
        }) : <FreeField label={name} value={item} copy={copy} disabled={disabled}
          onTyping={onTyping} update={change} />}
        {!disabled && <div className="property-actions">
          <button type="button" className="icon-button" aria-label={`${copy.renameProperty}: ${name}`} title={copy.renameProperty}
            onClick={() => { setRenaming(renaming === name ? null : name); setRenameKey(name) }}><Pencil size={15} /></button>
          <Remove copy={copy} disabled={disabled} remove={() => {
            const next = { ...entries }
            delete next[name]
            if (renaming === name) setRenaming(null)
            update(next)
          }} />
        </div>}
        {!disabled && renaming === name && <div className="property-rename rule-reference-row">
          {keyInput ? keyInput(renameKey, setRenameKey) : <label className="field"><span>{copy.propertyName}</span><input autoFocus value={renameKey}
            onChange={(event) => setRenameKey(event.target.value)}
            onKeyDown={(event) => { if (event.key === 'Enter') { event.preventDefault(); rename() } }} /></label>}
          <button type="button" className="secondary" disabled={!canRename} onClick={rename}>{copy.renameProperty}</button>
        </div>}
      </div>
    })}
    {!disabled && <div className="property-add">
      {keyInput ? keyInput(key, setKey) : <label className="field"><span>{copy.propertyName}</span><input value={key} onChange={(event) => setKey(event.target.value)}
        onKeyDown={(event) => { if (event.key === 'Enter') { event.preventDefault(); add() } }} /></label>}
      {!typed && <ValueType label={copy.valueType} kind={kind} copy={copy} disabled={disabled} select={(next) => { if (next) setKind(next) }} />}
      <button type="button" className="secondary" disabled={!newKey || Object.hasOwn(entries, newKey)} onClick={add}>
        <Plus size={15} />{copy.addProperty}
      </button>
    </div>}
  </div>
}

function Remove({ copy, disabled, remove }: { copy: RuleCopy; disabled: boolean; remove: () => void }) {
  return !disabled && <button type="button" className="icon-button" aria-label={copy.removeItem}
    title={copy.removeItem} onClick={remove}><X size={15} /></button>
}
