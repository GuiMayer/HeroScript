import { useDeferredValue, useId, useRef, useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import { Plus, Search, Tag, X } from 'lucide-react'
import { api, settingPath, type Card, type Definition, type Schemas } from './api/client'
import type { CardCopy } from './card-copy'
import { array, object, propertyKey, same, string, type Frame } from './editor-state'
import { TextField } from './TextField'
import { CardComponents } from './CardComponents'
import type { Locale } from './i18n'
import { JsonSource } from './JsonSource'
import { stringifyAuthoringJson } from './authoring-json'
import { DiagnosticField } from './DiagnosticField'
import { isWithin, type DiagnosticFocus } from './diagnostic-target'
import { InheritedField } from './InheritedField'

export const rarities = ['Common', 'Uncommon', 'Rare', 'Legendary'] as const
export function rarityLabel(value: string, copy: CardCopy) {
  const index = rarities.findIndex((rarity) => rarity.toLowerCase() === value.toLowerCase())
  return [copy.common, copy.uncommon, copy.rare, copy.legendary][index] ?? value
}

export function titleOf(definition: Definition, fallback: string) {
  return string(object(definition.metadata).displayName) || fallback
}

function TagPicker({ tags, add, remove, disabled, apiKey, setting, copy }: {
  tags: string[]; add: (tag: string) => void; remove: (tag: string) => void; disabled: boolean
  apiKey: string; setting: string; copy: CardCopy
}) {
  const [text, setText] = useState('')
  const [open, setOpen] = useState(false)
  const target = useRef<HTMLInputElement>(null)
  const id = useId()
  const query = useDeferredValue(text.trim())
  const suggestions = useQuery({
    queryKey: ['authoring', setting, 'tags', query],
    enabled: open && !disabled,
    queryFn: ({ signal }) => api<Schemas['CardAuthoringTagAuthoringPage']>(apiKey,
      `${settingPath(setting)}/references/tags?limit=20&q=${encodeURIComponent(query)}`, { signal }),
  })
  const known = (suggestions.data?.data.items ?? []).flatMap((tag) => tag.id ? [tag.id] : [])
  const available = known.filter((tag) => !tags.includes(tag))
  const typed = text.trim()
  function choose(tag: string) {
    if (!tag || tags.includes(tag)) return
    add(tag)
    setOpen(false)
    setText('')
  }

  return <>
    <div className="tag-row">{tags.length ? tags.map((tag, index) => <span className="tag-chip" key={`${tag}-${index}`}>
      <Tag size={12} />{tag}{!disabled && <button type="button" onClick={() => remove(tag)} aria-label={`${copy.removeTag}: ${tag}`}><X size={13} /></button>}
    </span>) : <span className="muted">{copy.noTags}</span>}</div>
    {!disabled && <div className="tag-picker" onBlur={(event) => {
      if (!event.currentTarget.contains(event.relatedTarget)) setOpen(false)
    }}>
      <div className="with-icon"><Search size={15} />
        <input ref={target} aria-label={copy.tagSearch} aria-expanded={open} aria-controls={open ? id : undefined}
          placeholder={copy.tagSearch} value={text} onFocus={() => setOpen(true)}
          onChange={(event) => { setOpen(true); setText(event.target.value) }}
          onKeyDown={(event) => {
            if (event.key === 'Enter' && typed) { event.preventDefault(); choose(typed) }
            if (event.key === 'Escape') setOpen(false)
          }} />
        <button className="icon-button" aria-label={copy.addTag} type="button" onClick={() => typed ? choose(typed) : target.current?.focus()}>
          <Plus size={17} />
        </button>
      </div>
      {open && <div className="tag-menu" id={id} aria-busy={suggestions.isFetching}>
        {available.map((tag) => <button type="button" key={tag} onClick={() => choose(tag)}><Tag size={14} />{tag}<small>{copy.existingTag}</small></button>)}
        {typed && !known.includes(typed) && !tags.includes(typed) &&
          <button type="button" onClick={() => choose(typed)}><Plus size={15} /><span>{copy.createTag} <strong>{typed}</strong></span></button>}
        {!available.length && !typed && <span className="muted tag-hint">{copy.tagHint}</span>}
      </div>}
    </div>}
  </>
}

export function Overview({ value, inherited, disabled, typing, apiKey, setting, copy, update, onTyping, focus }: EditorFields) {
  const metadataKey = propertyKey(value, 'metadata')
  const metadata = object(value[metadataKey])
  const tagsKey = propertyKey(value, 'tags')
  const rarityKey = propertyKey(value, 'rarity')
  const nameKey = propertyKey(metadata, 'displayName')
  const descriptionKey = propertyKey(metadata, 'description')
  const tags = array(value[tagsKey]).filter((tag): tag is string => typeof tag === 'string')
  const changeMeta = (key: string, next: string) => update({ ...value, [metadataKey]: { ...metadata, [key]: next } })
  const inheritedMeta = inherited && object(inherited[propertyKey(inherited, 'metadata')])
  const field = (name: string, label: string, parent: Definition, base: Definition | undefined, nested = false) => {
    const key = propertyKey(parent, name)
    const baseline = base?.[propertyKey(base, name)]
    return {
      label, value: parent[key],
      inherited: base ? { value: baseline, copy, restoringDisabled: disabled || typing } : undefined,
      restore: () => {
        const next = { ...parent }
        if (baseline === undefined) delete next[key]
        else next[key] = baseline
        if (!nested) update(next)
        else {
          const definition = { ...value, [metadataKey]: next }
          if (!Object.keys(next).length && inherited?.[propertyKey(inherited, 'metadata')] === undefined)
            delete definition[metadataKey]
          update(definition)
        }
      },
    }
  }
  return <div className="field-sections">
    <section className="form-section">
      <h3>{copy.basics}</h3>
      <div className="field-grid">
        <DiagnosticField focus={focus && isWithin(focus.path, '/metadata/displayName') ? focus : undefined}>
          <InheritedField {...field('displayName', copy.name, metadata, inheritedMeta, true)}>
            <TextField label={copy.name} value={string(metadata[nameKey])} disabled={disabled} onTyping={onTyping} onCommit={(v) => changeMeta(nameKey, v)} />
          </InheritedField>
        </DiagnosticField>
        <DiagnosticField focus={focus && isWithin(focus.path, '/rarity') ? focus : undefined}>
          <InheritedField {...field('rarity', copy.rarity, value, inherited)}>
            <label className="field"><span>{copy.rarity}</span>
              <select value={rarities.find((rarity) => rarity.toLowerCase() === string(value[rarityKey]).toLowerCase()) ?? 'Common'}
                disabled={disabled} onChange={(event) => update({ ...value, [rarityKey]: event.target.value })}>
                {rarities.map((rarity) => <option key={rarity} value={rarity}>{rarityLabel(rarity, copy)}</option>)}
              </select>
            </label>
          </InheritedField>
        </DiagnosticField>
      </div>
      <DiagnosticField focus={focus && isWithin(focus.path, '/metadata/description') ? focus : undefined}>
        <InheritedField {...field('description', copy.description, metadata, inheritedMeta, true)}>
          <TextField label={copy.description} value={string(metadata[descriptionKey])} disabled={disabled} multiline onTyping={onTyping}
            onCommit={(v) => changeMeta(descriptionKey, v)} />
        </InheritedField>
      </DiagnosticField>
    </section>
    <section className="form-section">
      <h3>{copy.tags}</h3><p className="muted">{copy.tagHint}</p>
      <DiagnosticField focus={focus && isWithin(focus.path, '/tags') ? focus : undefined}>
        <InheritedField {...field('tags', copy.tags, value, inherited)}>
          <TagPicker tags={tags} add={(tag) => update({ ...value, [tagsKey]: [...array(value[tagsKey]), tag] })}
            remove={(tag) => update({ ...value, [tagsKey]: array(value[tagsKey]).filter((item) => item !== tag) })}
            {...{ disabled, apiKey, setting, copy }} />
        </InheritedField>
      </DiagnosticField>
    </section>
  </div>
}

export function Rules(props: EditorFields & { locale: Locale; draft: Frame }) {
  const key = propertyKey(props.value, 'components')
  const baseline = props.inherited?.[propertyKey(props.inherited, 'components')]
  return <div className="field-sections">
    {props.inherited && !same(props.value[key], baseline) && <div className="notice">
      <span>{props.copy.customized} · {props.copy.components}</span>
      <button type="button" className="text-button" disabled={props.disabled || props.typing} onClick={() => {
        const next = { ...props.value }
        if (baseline === undefined) delete next[key]
        else next[key] = baseline
        props.update(next)
      }}>{props.copy.restoreComponents}</button>
    </div>}
    <CardComponents {...props} group="rules" />
    <CardComponents {...props} group="effects" />
  </div>
}

export function Advanced({ value, disabled, copy, update, apiKey, setting, detail }: EditorFields & { detail?: Card }) {
  const [schemaOpen, setSchemaOpen] = useState(false)
  const schema = useQuery({
    queryKey: ['authoring', setting, 'schema', 'cards'], enabled: schemaOpen,
    queryFn: ({ signal }) => api<Schemas['AuthoringKindSchema']>(apiKey, `${settingPath(setting)}/kinds/cards/schema`, { signal }),
  })
  return <div className="field-sections">
    <JsonSource value={value} disabled={disabled} update={update} copy={copy} label={copy.fullDefinition}
      identity="cardId" id={value[propertyKey(value, 'cardId')]} invalidMessage={copy.invalidJson} />
    {detail?.source !== undefined && <details className="form-section disclosure">
      <summary>{copy.compare}</summary><pre className="code-view">{stringifyAuthoringJson(detail.source, 2)}</pre>
    </details>}
    {detail?.effective !== undefined && <details className="form-section disclosure">
      <summary>{copy.effective}</summary><pre className="code-view">{stringifyAuthoringJson(detail.effective, 2)}</pre>
    </details>}
    {!!detail?.provenance?.length && <details className="form-section disclosure">
      <summary>{copy.provenance}</summary><pre className="code-view">{JSON.stringify(detail.provenance, null, 2)}</pre>
    </details>}
    <details className="form-section disclosure" onToggle={(event) => setSchemaOpen(event.currentTarget.open)}>
      <summary>{copy.schema}</summary>
      {schema.data?.data.schema ? <pre className="code-view">{JSON.stringify(schema.data.data.schema, null, 2)}</pre>
        : <p className="muted">{schema.isError ? copy.sourceUnavailable : '…'}</p>}
    </details>
  </div>
}

interface EditorFields {
  value: Definition; disabled: boolean; copy: CardCopy; apiKey: string; setting: string
  inherited?: Definition
  typing: boolean
  update: (value: Definition) => void
  onTyping: (dirty: boolean) => void
  focus?: DiagnosticFocus
}
