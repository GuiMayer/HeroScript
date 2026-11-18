import { useDeferredValue, useEffect, useState } from 'react'
import { useInfiniteQuery, useQuery } from '@tanstack/react-query'
import { Boxes, ChevronRight, FileText, GitBranch, Layers3, LockKeyhole, Pencil, Plus, RotateCcw, Search, Settings2, Sparkles, Swords, Trash2, Users } from 'lucide-react'
import { AuthoringActions, AuthoringReceipt } from './AuthoringActions'
import { api, ApiError, definitionPath, definitionsPath, settingPath, type Definition, type Schemas } from './api/client'
import { cardCopy, type CardCopy } from './card-copy'
import type { CardsProps } from './Cards'
import { array, object, propertyKey, same, string } from './editor-state'
import { preference } from './i18n'
import { Modal } from './Modal'
import { NewWritingPackage } from './NewWritingPackage'
import { JsonSource } from './JsonSource'
import { stringifyAuthoringJson } from './authoring-json'
import { PropertyFields } from './FreeField'
import { RuleFields } from './RuleFields'
import { definitionCopy, definitionGroups, type DefinitionKind, type DefinitionSection, type RuleCopy } from './rule-copy'
import { useCardDrafts } from './use-card-drafts'

type Props = CardsProps & { section: DefinitionSection }

export default function RulesEditor(props: Props) {
  return <RuleWorkspace key={`${props.section}/${props.section === 'settings' ? '' : props.setting}`} {...props} />
}

function titleOf(value: Definition | undefined, id: string) {
  const packageId = value && string(value[propertyKey(value, 'packageId')])
  if (value && packageId) return [packageId, string(value[propertyKey(value, 'version')])].filter(Boolean).join(' · ')
  return value && (string(value[propertyKey(value, 'displayName')]) || string(value[propertyKey(value, 'name')])) || id
}

function RuleWorkspace({ apiKey, setting: activeSetting, writable, locale, copy: shell, workspace, section }: Props) {
  const setting = section === 'settings' ? '' : activeSetting
  const { copy, labels } = definitionCopy(locale, section)
  const groups = definitionGroups[section]
  const Icon = { resources: Boxes, effects: Sparkles, rules: GitBranch, entities: Users, decks: Layers3, runs: Swords, settings: Settings2 }[section]
  const actionsCopy = cardCopy[locale]
  const [kind, setKind] = useState<DefinitionKind>(groups[0][0])
  const [selected, setSelected] = useState(section === 'settings' ? activeSetting : '')
  const [customizingDefinition, setCustomizingDefinition] = useState('')
  const [search, setSearch] = useState('')
  const [tab, setTab] = useState<'fields' | 'advanced'>('fields')
  const [typing, setTyping] = useState(false)
  const [hot, setHot] = useState(() => preference('mode', ['manual', 'hot'], 'manual') === 'hot')
  const [modal, setModal] = useState<'create' | 'delete' | 'discard' | 'writing-package' | null>(null)
  useEffect(() => {
    if (section === 'settings') {
      setKind('settings')
      setSelected(activeSetting)
      setSearch('')
      setTab('fields')
    }
  }, [activeSetting, section])
  const actions = useCardDrafts(workspace, setting, apiKey, hot)
  const { draft } = actions
  const searchTerm = useDeferredValue(search.trim())
  const kinds = useQuery({
    queryKey: ['authoring', setting, 'kinds'],
    queryFn: ({ signal }) => api<Schemas['AuthoringKind'][]>(apiKey, `${settingPath(setting)}/kinds`, { signal }),
  })
  const list = useInfiniteQuery({
    queryKey: ['authoring', setting, 'definitions', kind, searchTerm], initialPageParam: '',
    queryFn: ({ signal, pageParam }) => api<Schemas['CardAuthoringSummaryAuthoringPage']>(apiKey,
      `${definitionsPath(setting, kind)}?${new URLSearchParams({
        q: searchTerm, limit: '30', ...(pageParam ? { cursor: pageParam } : {}),
      })}`, { signal }),
    getNextPageParam: (last) => last.data.nextCursor ?? undefined,
  })
  const schema = useQuery({
    queryKey: ['authoring', setting, 'schema', kind],
    queryFn: ({ signal }) => api<Schemas['AuthoringKindSchema']>(apiKey,
      `${settingPath(setting)}/kinds/${encodeURIComponent(kind)}/schema`, { signal }),
  })
  const identity = schema.data?.data.identityProperty ?? undefined
  const raw = !!schema.data && !object(schema.data.data.schema).properties
  const showFields = tab === 'fields'
  const rows = new Map<string, Schemas['CardAuthoringSummary']>()
  for (const { data } of list.data?.pages ?? [])
    for (const item of data.items ?? []) if (item.definitionId) rows.set(item.definitionId, item)
  for (const { operation } of draft.frame)
    if (operation.kind === kind && !rows.has(operation.definitionId) &&
        operation.definitionId.toLowerCase().includes(searchTerm.toLowerCase()))
      rows.set(operation.definitionId, { definitionId: operation.definitionId, canEdit: true, canDelete: true })

  const firstEditable = writable ? Array.from(rows).find(([, item]) => item.canEdit)?.[0] : undefined
  const id = selected || firstEditable || rows.keys().next().value || ''
  const staged = draft.frame.find(({ operation }) => operation.kind === kind && operation.definitionId === id)
  const created = staged?.operation.operation === 'create'
  const deleted = staged?.operation.operation === 'delete'
  const detail = useQuery({
    queryKey: ['authoring', setting, 'definition', kind, id],
    enabled: !!id && !created,
    queryFn: ({ signal }) => api<Schemas['AuthoringDefinitionDetail']>(apiKey, definitionPath(setting, kind, id), { signal }),
  })
  useEffect(() => {
    if (selected && !staged && !detail.isFetching && detail.error instanceof ApiError && detail.error.status === 404)
      setSelected('')
  }, [selected, staged, detail.error, detail.isFetching])
  const source = detail.data?.data
  const customizable = section === 'rules' && !!source?.item?.canCustomize
  const inherited = customizable ? object(source?.source) : undefined
  const effective = source ? object(customizable ? source.effective : source.source) : undefined
  const selectionKey = `${kind}/${id}`
  const customizing = customizable && (customizingDefinition === selectionKey || !!source?.item?.hasCustomization ||
    staged?.operation.operation === 'customize')
  const value = staged?.operation.operation === 'resetCustomization' ? inherited
    : staged?.operation.definition ? object(staged.operation.definition) : effective
  const personalized = !!inherited && !!value && !same(value, inherited)
  const listVersion = list.data?.pages[0]?.version ?? list.data?.pages[0]?.data.sourceVersion ?? undefined
  const version = detail.data?.version ?? listVersion
  const locked = !writable || !!draft.busy || !!draft.attempt || !!draft.intent
  const editable = !locked && !deleted && (created || !!source?.item?.canEdit || customizing)
  const canCreate = (name: string) => !locked && groups.some((group) => group.some((item) => item === name)) &&
    !!kinds.data?.data.some((item) => item.kind === name && item.canCreate)
  const canDelete = !locked && !deleted && (created || !!source?.item?.canDelete)

  function chooseKind(next: DefinitionKind) {
    setKind(next)
    setSelected('')
    setSearch('')
    setTab('fields')
    setTyping(false)
  }

  function choose(id: string) {
    setSelected(id)
    setTab('fields')
    setTyping(false)
  }

  function update(definition: Definition) {
    actions.edit({
      operation: { kind, operation: created ? 'create' : customizing ? 'customize' : 'update', definitionId: id, definition,
        sourcePackage: staged?.operation.sourcePackage },
      original: effective,
    }, version)
  }

  function restoreInherited() {
    if (!inherited) return
    setCustomizingDefinition(selectionKey)
    if (!source?.item?.hasCustomization) {
      update(inherited)
      return
    }
    actions.edit({ operation: { kind, operation: 'resetCustomization', definitionId: id },
      original: effective }, version)
  }

  function create(id: string, copySource: boolean, sourcePackage?: string) {
    if (!schema.data || rows.has(id) || id === '.' || id === '..') return false
    const definition = { ...(copySource ? value : {}) }
    if (identity) definition[propertyKey(definition, identity)] = id
    if (kind === 'settings') delete definition[propertyKey(definition, 'authoringPackageId')]
    if (kind === 'packages') definition[propertyKey(definition, 'packageId')] = id
    if (!actions.edit({ operation: { kind, operation: 'create', definitionId: id, definition, sourcePackage } }, listVersion)) return false
    choose(id)
    setSearch('')
    setModal(null)
    return true
  }

  function remove() {
    actions.edit({
      operation: { kind, operation: 'delete', definitionId: id },
      original: source ? object(source.source) : undefined,
    }, version)
    if (created || hot) setSelected('')
    setModal(null)
  }

  function createWritingPackage(directory: string, packageId: string, packageVersion: string) {
    if (!value || !editable || !canCreate('packages') || !directory || !packageId || !packageVersion ||
      draft.frame.some(({ operation }) => operation.kind === 'packages' && operation.definitionId === directory)) return false
    const packagesKey = propertyKey(value, 'packages')
    const definition = { ...value,
      [packagesKey]: [...array(value[packagesKey]), { packageId, versionRange: packageVersion }],
      [propertyKey(value, 'authoringPackageId')]: packageId,
    }
    if (!actions.editMany([
      { operation: { kind: 'packages', operation: 'create', definitionId: directory,
        definition: { packageId, version: packageVersion } } },
      { operation: { kind: 'settings', operation: created ? 'create' : 'update', definitionId: id,
        definition, sourcePackage: staged?.operation.sourcePackage }, original: source ? object(source.source) : undefined },
    ], version)) return false
    setModal(null)
    return true
  }

  return <div className="cards-page">
    <header className="page-heading">
      <div><h1>{shell[section]}</h1><p>{copy.intro}</p></div>
      <button className="primary" disabled={!canCreate(kind) || !schema.data || !listVersion}
        onClick={() => setModal('create')}><Plus size={17} />{copy.newRule}</button>
    </header>
    <AuthoringActions actions={actions} copy={actionsCopy} version={listVersion} writable={writable} label={copy.editing}
      hot={hot} setHot={setHot} typing={typing} modal={!!modal} onDiscard={() => setModal('discard')} />
    <div className="cards-layout">
      <section className="card-catalog" aria-label={copy.rules}>
        <div className="catalog-header rule-catalog-header">
          <h2>{copy.rules}</h2>
          <label className="field"><span>{copy.kind}</span>
            <select value={kind} onChange={(event) => chooseKind(event.target.value as DefinitionKind)}>
              {groups.map((group, index) => <optgroup key={group[0]} label={index === 0 ? copy.gameplay : copy.policies}>
                {group.map((key) => <option key={key} value={key}>{labels[key]}</option>)}
              </optgroup>)}
            </select>
          </label>
          <label className="with-icon"><Search size={16} /><input type="search" aria-label={actionsCopy.search}
            placeholder={actionsCopy.search} value={search} onChange={(event) => setSearch(event.target.value)} /></label>
        </div>
        {list.isPending ? <Loading label={shell.loading} /> : <>
          {list.isError ? <p className="catalog-message error" role="alert">{actionsCopy.sourceUnavailable} <button
            className="text-button" onClick={() => void list.refetch()}>{shell.retry}</button></p>
            : !rows.size && <p className="catalog-message muted">{copy.empty}</p>}
          <div className="card-list">
            {Array.from(rows, ([rowId, item]) => <button className={`card-row ${rowId === id ? 'active' : ''}`}
              key={rowId} aria-current={rowId === id ? 'true' : undefined} onClick={() => choose(rowId)}>
              <span className="row-icon"><Icon size={17} /></span>
              <span className="row-label"><strong>{titleOf(rowId === id ? value : undefined, item.displayName || rowId)}</strong>
                <small>{rowId} · {item.packageId ?? actionsCopy.draft}</small></span>
              <ChevronRight size={15} className="row-chevron" />
            </button>)}
          </div>
          {list.hasNextPage && <button className="secondary load-more" disabled={list.isFetchingNextPage}
            onClick={() => void list.fetchNextPage()}>{list.isFetchingNextPage ? shell.loading : actionsCopy.loadMore}</button>}
        </>}
      </section>

      <section className="card-detail" aria-label={copy.definition}>
        {id && !created && detail.isPending ? <Loading label={shell.loading} /> : value ? <>
          <header className="detail-heading">
            <span className="detail-icon"><Icon size={23} /></span>
            <div className="detail-title"><h2>{titleOf(value, id)}</h2><code>{id}</code></div>
            {customizable && !customizing && <button type="button" className="secondary" disabled={locked || !schema.data}
              onClick={() => { setCustomizingDefinition(selectionKey); setTab('fields') }}>
              <Pencil size={15} />{actionsCopy.customize}
            </button>}
            {canDelete && <button className="icon-button danger-button" aria-label={copy.delete} title={copy.delete}
              onClick={() => setModal('delete')}><Trash2 size={17} /></button>}
          </header>
          <div className="detail-meta">
            <span className="meta-label">{labels[kind]}</span>
            {source?.item?.packageId && <span className="meta-source"><FileText size={14} />{source.item.packageId}</span>}
            {deleted ? <span className="meta-label error">{actionsCopy.deletion}</span> : created
              ? <span className="meta-label">{actionsCopy.draft}</span>
              : customizable ? <span className="meta-label">{personalized ? actionsCopy.customized : copy.inheritedDefinition}</span>
              : (!writable || !source?.item?.canEdit) && <span className="meta-label"><LockKeyhole size={13} />{copy.inherited}</span>}
            {personalized && <button type="button" className="text-button" disabled={!editable || typing}
              onClick={restoreInherited}><RotateCcw size={14} />{copy.restoreDefinition}</button>}
          </div>
          <nav className="detail-tabs" aria-label={copy.definition}>
            <button className={showFields ? 'chosen' : ''} aria-current={showFields ? 'page' : undefined}
              onClick={() => { setTyping(false); setTab('fields') }}>{copy.form}</button>
            <button className={!showFields ? 'chosen' : ''} aria-current={!showFields ? 'page' : undefined}
              onClick={() => { setTyping(false); setTab('advanced') }}>{actionsCopy.advanced}</button>
          </nav>
          <div className="detail-content">
            {showFields
              ? raw ? <section className="form-section">
                <h3>{copy.properties}</h3>
                <p className="muted">{copy.rawNotice}</p>
                <PropertyFields key={`${kind}/${id}`} value={value} update={update}
                  copy={copy} disabled={!editable} onTyping={setTyping} />
              </section> : schema.data ? <RuleFields key={`${kind}/${id}`} identity={identity} value={value} update={update}
                schema={schema.data.data.schema} context={{
                  key: apiKey, setting, referenceSetting: section === 'settings' ? activeSetting : undefined,
                  disabled: !editable, locale, copy, root: value, draft: draft.frame,
                  inherited: inherited && { path: '', value: inherited, copy: actionsCopy, restoringDisabled: !editable || typing },
                  full: customizable || (section !== 'rules' && section !== 'effects'),
                  reorder: customizable || kind === 'formulas' || kind === 'runs', schemaRoot: schema.data.data.schema,
                  refs: schema.data.data.references ?? {}, onTyping: setTyping, canCreate,
                  onCreateReference: (target) => { chooseKind(target as DefinitionKind); setModal('create') },
                  onCreateWritingPackage: kind === 'settings' && canCreate('packages') ? () => setModal('writing-package') : undefined,
                }} /> : schema.isError ? <p className="error">{actionsCopy.sourceUnavailable}</p> : <Loading label={shell.loading} />
              : <RuleAdvanced key={`${kind}/${id}`} value={value} source={source} disabled={!editable || !schema.data}
                id={id} identity={identity} update={update} copy={copy} actionsCopy={actionsCopy} showEffective={section !== 'settings'} />}
          </div>
        </> : <div className="detail-empty"><span className="empty-icon"><Icon size={23} /></span>
          <p>{detail.isError ? actionsCopy.sourceUnavailable : copy.select}</p>
          {detail.isError && <button className="secondary" onClick={() => void detail.refetch()}>{shell.retry}</button>}
        </div>}
      </section>
    </div>
    <AuthoringReceipt receipt={draft.receipt} copy={actionsCopy} />
    {modal === 'writing-package' && <NewWritingPackage copy={copy} closeLabel={shell.close}
      cancelLabel={actionsCopy.cancel} close={() => setModal(null)} ready={editable && canCreate('packages')}
      create={createWritingPackage} />}
    {modal === 'create' && <NewDefinition key={kind} close={() => setModal(null)} closeLabel={shell.close} copy={copy}
      actionsCopy={actionsCopy} title={labels[kind]} template={value ? titleOf(value, id) : ''}
      ready={!!schema.data && !!listVersion && canCreate(kind)} create={create}
      idLabel={kind === 'packages' ? copy.packageDirectory : copy.id} apiKey={apiKey} needsOwner={kind === 'settings'}
      draftPackages={draft.frame.filter(({ operation }) => operation.kind === 'packages' && operation.operation === 'create')
        .map(({ operation }) => operation.definitionId)} />}
    {(modal === 'delete' || modal === 'discard') && <Modal title={modal === 'delete' ? copy.delete : actionsCopy.discard}
      closeLabel={shell.close} onClose={() => setModal(null)}>
      <p className="muted">{modal === 'delete' ? copy.confirmDelete : actionsCopy.discardPrompt}</p>
      <div className="dialog-actions">
        <button className="secondary" onClick={() => setModal(null)}>{actionsCopy.cancel}</button>
        <button className="primary destructive" onClick={() => {
          if (modal === 'delete') remove()
          else { actions.discard(); setSelected(''); setModal(null) }
        }}>{modal === 'delete' ? copy.delete : actionsCopy.discard}</button>
      </div>
    </Modal>}
  </div>
}

function Loading({ label }: { label: string }) {
  return <div className="row-loading" aria-label={label} aria-busy="true" role="status">
    <div className="skeleton" /><div className="skeleton" /><div className="skeleton" />
  </div>
}

function RuleAdvanced({ id, value, identity, source, disabled, update, copy, actionsCopy, showEffective }: {
  id: string; value: Definition; identity?: string; source?: Schemas['AuthoringDefinitionDetail']; disabled: boolean
  update: (next: Definition) => void; copy: RuleCopy; actionsCopy: CardCopy; showEffective: boolean
}) {
  return <div className="field-sections">
    <JsonSource value={value} disabled={disabled} update={update} copy={actionsCopy} label={copy.source}
      id={id} identity={identity} invalidMessage={identity ? copy.invalidJson : copy.rawInvalid} />
    {source?.source !== undefined && <details className="form-section disclosure"><summary>{copy.latest}</summary>
      <pre className="code-view">{stringifyAuthoringJson(source.source, 2)}</pre></details>}
    {showEffective && source?.effective !== undefined && <details className="form-section disclosure"><summary>{actionsCopy.effective}</summary>
      <pre className="code-view">{stringifyAuthoringJson(source.effective, 2)}</pre></details>}
    {!!source?.provenance?.length && <details className="form-section disclosure"><summary>{actionsCopy.provenance}</summary>
      <pre className="code-view">{stringifyAuthoringJson(source.provenance, 2)}</pre></details>}
  </div>
}

function NewDefinition({ close, closeLabel, copy, actionsCopy, title, template, ready, create, idLabel, apiKey, needsOwner, draftPackages }: {
  close: () => void; closeLabel: string; copy: RuleCopy; actionsCopy: CardCopy; title: string; template: string; ready: boolean
  create: (id: string, copySource: boolean, sourcePackage?: string) => boolean
  idLabel: string; apiKey: string; needsOwner: boolean; draftPackages: string[]
}) {
  const [useTemplate, setTemplate] = useState(idLabel !== copy.packageDirectory)
  const [invalid, setInvalid] = useState(false)
  const [owner, setOwner] = useState('')
  const ownerSearch = useDeferredValue(owner.trim())
  const owners = useQuery({
    queryKey: ['authoring', '', 'references', 'package-sources', ownerSearch], enabled: needsOwner,
    queryFn: ({ signal }) => api<Schemas['AuthoringReferenceAuthoringPage']>(apiKey,
      `/workspace/references/package-sources?limit=50&q=${encodeURIComponent(ownerSearch)}`, { signal }),
  })
  return <Modal title={copy.newRule} onClose={close} closeLabel={closeLabel}>
    <p className="muted">{title} · {copy.dialogHint}</p>
    <form onSubmit={(event) => {
      event.preventDefault()
      const data = new FormData(event.currentTarget)
      if (!create(String(data.get('id')).trim(), !!template && useTemplate, needsOwner ? owner.trim() : undefined)) setInvalid(true)
    }}>
      <label htmlFor="new-definition-id">{idLabel}</label>
      <input id="new-definition-id" name="id" required autoFocus maxLength={128} pattern={'[a-zA-Z0-9._\\-]+'} />
      {needsOwner && <>
        <label htmlFor="definition-owner">{copy.sourcePackage}</label>
        <input id="definition-owner" list="definition-owners" required value={owner} onChange={(event) => setOwner(event.target.value)} />
        <datalist id="definition-owners">
          {[...new Set([...(owners.data?.data.items ?? []).flatMap((entry) => entry.id ? [entry.id] : []), ...draftPackages])]
            .map((name) => <option key={name} value={name} />)}
        </datalist>
      </>}
      {!!template && <label className="template-option"><input type="checkbox" checked={useTemplate}
        onChange={(event) => setTemplate(event.target.checked)} /><span>{actionsCopy.template} <strong>{template}</strong></span></label>}
      {invalid && <p className="error" role="alert">{copy.invalidId}</p>}
      <div className="dialog-actions"><button className="secondary" type="button" onClick={close}>{actionsCopy.cancel}</button>
        <button className="primary" type="submit" disabled={!ready}><Plus size={15} />{copy.newRule}</button></div>
    </form>
  </Modal>
}
