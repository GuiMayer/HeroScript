import { useEffect } from 'react'
import { AlertCircle, CheckCircle2, ChevronDown, CircleCheck, Redo2, Save, Undo2, Zap } from 'lucide-react'
import type { Receipt } from './api/client'
import { ApiError } from './api/client'
import type { CardCopy } from './card-copy'
import { RebaseConflict, type useCardDrafts } from './use-card-drafts'

export function AuthoringActions({ actions, copy, version: listVersion, writable, hot, setHot, typing = false,
  modal, onDiscard, label = copy.details }: {
  actions: ReturnType<typeof useCardDrafts>; copy: CardCopy; version?: string; writable: boolean
  hot: boolean; setHot: (hot: boolean) => void; typing?: boolean; modal: boolean; onDiscard: () => void; label?: string
}) {
  const { draft } = actions
  const pending = !!draft.frame.length || !!draft.intent
  const editLocked = !writable || !!draft.busy || !!draft.attempt || !!draft.intent
  const canUndo = !editLocked && (!!draft.past.length || (!draft.frame.length && !!draft.undo.length && !!listVersion))
  const canRedo = !editLocked && (!!draft.future.length || (!draft.frame.length && !!draft.redo.length && !!listVersion))

  useEffect(() => {
    function onKey(event: KeyboardEvent) {
      if (modal || event.defaultPrevented || event.isComposing ||
        (!(event.ctrlKey || event.metaKey)) || event.altKey || event.key.toLowerCase() !== 'z') return
      if (event.target instanceof HTMLElement && event.target.closest('input, textarea, [contenteditable="true"], dialog')) return
      if (event.shiftKey ? canRedo : canUndo) {
        event.preventDefault()
        actions.turn(event.shiftKey ? 'redo' : 'undo', listVersion)
      }
    }
    window.addEventListener('keydown', onKey)
    return () => window.removeEventListener('keydown', onKey)
  })

  const status = draft.busy === 'save' ? copy.saving : draft.busy === 'validate' ? copy.validating :
    draft.busy === 'rebase' ? copy.rebasing : draft.attempt ? copy.waiting :
    pending || typing ? copy.pending : draft.receipt ? copy.saved : copy.noChanges
  const diagnostic = draft.error instanceof ApiError ? draft.error : undefined
  const hasConflict = diagnostic?.status === 412 || draft.error instanceof RebaseConflict
  const diagnostics = diagnostic?.problem.validation?.diagnostics ?? []
  return <>
    <section className="actionbar" aria-label={label}>
      <div className="actionbar-left">
        <button className="icon-button" aria-label={copy.undo} title={`${copy.undo} (Ctrl+Z)`} disabled={!canUndo}
          onClick={() => actions.turn('undo', listVersion)}><Undo2 size={18} /></button>
        <button className="icon-button" aria-label={copy.redo} title={`${copy.redo} (Ctrl+Shift+Z)`} disabled={!canRedo}
          onClick={() => actions.turn('redo', listVersion)}><Redo2 size={18} /></button>
        <span className="divider" />
        <label className="mode-control" title={hot ? copy.hot : copy.manual}>
          <Zap size={15} /><select aria-label={copy.mode} value={hot ? 'hot' : 'manual'}
            disabled={!writable || !!draft.busy || pending || typing || !!draft.attempt} onChange={(event) => {
              setHot(event.target.value === 'hot')
              localStorage.setItem('heroscript.editor.mode', event.target.value)
            }}><option value="manual">{copy.manual}</option><option value="hot">{copy.hot}</option></select>
          <ChevronDown size={13} />
        </label>
      </div>
      <span className={`save-status ${draft.error ? 'problem' : draft.receipt && !pending ? 'done' : ''}`} role="status">
        {draft.receipt && !pending ? <CheckCircle2 size={15} /> : <span className="dot" />}{status}
      </span>
      <div className="actionbar-right">
        <button className="secondary" disabled={!writable || (!pending && !typing) || !!draft.busy || !!draft.attempt}
          onClick={() => void actions.validate()}><CircleCheck size={15} />{copy.validate}</button>
        <button className="primary" disabled={!writable || (!pending && !typing) || !!draft.busy}
          onClick={() => void actions.save()}><Save size={15} />{draft.attempt && !draft.busy ? copy.retryWrite : copy.save}</button>
      </div>
    </section>
    {(draft.error || draft.intent || draft.valid || pending) && <div className={`notice ${draft.error ? 'notice-error' : ''}`}>
      <div className="notice-body">
        {draft.error ? <><AlertCircle size={17} /><div><strong>{draft.attempt ? copy.uncertain :
          draft.error instanceof RebaseConflict ? copy.rebaseConflict : hasConflict ? copy.conflict : copy.invalid}</strong>
          {diagnostic?.message && !hasConflict && <p>{diagnostic.message}</p>}
          {!!diagnostics.length && <ul>{diagnostics.map((item, index) => <li key={index}>
            {item.definitionId && <code>{item.definitionId} {item.jsonPointer || ''} </code>}
            {item.parameters?.message || item.code}
          </li>)}</ul>}</div></> : draft.intent ? <><Undo2 size={17} /><strong>{draft.intent.kind === 'undo' ? copy.undoStaged : copy.redoStaged}</strong></>
          : <><CircleCheck size={17} /><strong>{draft.valid ? copy.valid : `${copy.pending} · ${draft.frame.length}`}</strong></>}
      </div>
      {!draft.busy && draft.attempt && <button className="secondary" onClick={() => void actions.checkStatus()}>{copy.checkStatus}</button>}
      {!draft.busy && !draft.attempt && <div className="notice-actions">
        {hasConflict && draft.frame.length > 0 && <button className="secondary" onClick={() => void actions.rebase()}>{copy.rebase}</button>}
        {pending && <button className="text-button" onClick={() => draft.intent ? actions.discard() : onDiscard()}>
          {draft.intent ? copy.cancelIntent : copy.discard}</button>}
      </div>}
    </div>}
  </>
}

export function AuthoringReceipt({ receipt, copy }: { receipt?: Receipt; copy: CardCopy }) {
  return receipt ? <details className="receipt">
      <summary>{copy.changedFiles} / {copy.revisions}</summary>
      <ul>{receipt.changedFiles?.map((file) => <li key={file}><code>{file}</code></li>)}</ul>
      <dl>{Object.entries(receipt.activeRevisions ?? {}).map(([name, revision]) =>
        <div key={name}><dt>{name}</dt><dd><code>{revision}</code></dd></div>)}</dl>
    </details> : null
}
