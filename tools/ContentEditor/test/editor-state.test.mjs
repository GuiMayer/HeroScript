import test from 'node:test'
import assert from 'node:assert/strict'
import { Workspace, confirmed, history, mergeIndependent, stage, stageEntries } from '../src/editor-state.ts'
import { parseAuthoringJson, stringifyAuthoringJson } from '../src/authoring-json.ts'
import { readFile } from 'node:fs/promises'
import ts from 'typescript'

const diagnosticSource = await readFile(new URL('../src/diagnostic-target.ts', import.meta.url), 'utf8')
const diagnosticCode = ts.transpileModule(diagnosticSource, {
  compilerOptions: { module: ts.ModuleKind.ESNext, target: ts.ScriptTarget.ES2022 },
}).outputText.replace("'./editor-state'", JSON.stringify(new URL('../src/editor-state.ts', import.meta.url).href))
const { cardDiagnosticPath, diagnosticCardId } = await import('data:text/javascript;base64,' + Buffer.from(diagnosticCode).toString('base64'))

test('card diagnostics identify the component and the missing resource field', () => {
  const definition = { components: [{ type: 'effect', componentId: 'effect.0', effect: {
    type: 'MODIFY_RESOURCE', targetResource: 'missing_resource',
  } }] }
  const diagnostic = { kind: 'cards', definitionId: '', parameters: {
    message: 'cards/test_card/components/effect.0/effects[0]: references missing resources/missing_resource',
  } }
  assert.equal(diagnosticCardId(diagnostic), 'test_card')
  assert.equal(cardDiagnosticPath(diagnostic, definition), '/components/0/effect/targetResource')
  assert.equal(cardDiagnosticPath({ ...diagnostic, jsonPointer: '/rarity' }, definition), '/rarity')
  const chained = { components: [{ type: 'effect', componentId: 'effect.0', effect: {
    chainedEffects: [{ type: 'APPLY_STATUS', statusId: 'unknown_status' }],
  } }] }
  assert.equal(cardDiagnosticPath({ ...diagnostic, parameters: {
    message: 'cards/test_card/components/effect.0/effects[0]/chainedEffects[0]: references missing status-effects/unknown_status',
  } }, chained), '/components/0/effect/chainedEffects/0/statusId')
})

test('daily challenge seeds round-trip as exact numbers beyond the safe integer range', () => {
  const source = '{"Seed":18446744073709551615,"name":"Before"}'
  const challenge = parseAuthoringJson(source)
  assert.equal(challenge.Seed, 18446744073709551615n)
  assert.equal(stringifyAuthoringJson({ ...challenge, name: 'After' }), '{"Seed":18446744073709551615,"name":"After"}')
})

const create = (id, definition = {}) => ({
  operation: { kind: 'cards', operation: 'create', definitionId: id, definition },
})
const update = (id, original, definition) => ({
  operation: { kind: 'cards', operation: 'update', definitionId: id, definition },
  original,
})
const remove = (id) => ({ operation: { kind: 'cards', operation: 'delete', definitionId: id } })

test('restoring inherited values clears an unsaved patch but keeps the draft undoable', () => {
  const before = new Workspace().get('uno')
  const original = { tags: ['attack'] }
  const customize = (definition) => ({ original, operation: {
    kind: 'cards', operation: 'customize', definitionId: 'inherited_card', definition,
  } })
  const edited = stage(before, customize({ tags: ['attack', 'fire'] }), 'version')
  const restored = stage(edited, customize(original), 'version')
  assert.deepEqual(restored.frame, [])
  const undone = history(restored, 'undo')
  assert.deepEqual(undone.frame, edited.frame)
  assert.deepEqual(history(undone, 'redo').frame, [])
})

test('package creation and setting linkage undo and redo as a single action without losing other drafts', () => {
  const workspace = new Workspace()
  const pending = stage(workspace.get(''), create('pending'), 'original')
  const entries = [
    { operation: { kind: 'packages', operation: 'create', definitionId: 'new-package', definition: { packageId: 'new-package', version: '1.0.0' } } },
    { operation: { kind: 'settings', operation: 'update', definitionId: 'uno', definition: { authoringPackageId: 'new-package' } }, original: {} },
  ]
  const next = stageEntries(pending, entries, 'newer')
  assert.equal(next.version, 'original')
  assert.equal(next.frame.length, 3)
  assert.equal(next.past.length, pending.past.length + 1)
  const undone = history(next, 'undo')
  assert.deepEqual(undone.frame, pending.frame)
  assert.deepEqual(history(undone, 'redo').frame, next.frame)
  assert.equal(stageEntries(next, entries, 'newer'), next)
})

test('drafts belong to the selected game setting and retain their original ETag', () => {
  const workspace = new Workspace()
  const first = stage(workspace.get('uno'), create('one'), 'initial')
  workspace.set('uno', first)
  workspace.set('table', stage(workspace.get('table'), create('two'), 'other'))
  const edited = stage(workspace.get('uno'), update('one', {}, { tags: ['new'] }), 'later')
  assert.equal(edited.version, 'initial')
  assert.equal(edited.frame[0].operation.operation, 'create')
  assert.deepEqual(workspace.get('table').frame[0].operation, create('two').operation)
})

test('local undo and redo restore complete draft frames', () => {
  const original = new Workspace().get('uno')
  const draft = stage(original, create('one'), 'initial')
  const deleted = stage(draft, remove('one'), 'initial')
  assert.deepEqual(deleted.frame, [])
  const undo = history(deleted, 'undo')
  assert.deepEqual(undo.frame, draft.frame)
  assert.deepEqual(history(undo, 'redo').frame, [])
})

test('a card and a rule with the same identifier stay separate in one draft', () => {
  const draft = stage(new Workspace().get('uno'), create('shared'), 'version')
  const rule = { operation: { kind: 'flow-rules', operation: 'create', definitionId: 'shared',
    definition: { flowRulesId: 'shared' } } }
  const both = stage(draft, rule, 'version')
  assert.deepEqual(both.frame.map(({ operation }) => operation.kind), ['cards', 'flow-rules'])
  assert.deepEqual(history(both, 'undo').frame, draft.frame)
})

test('confirmed undo and redo use the authoring receipt IDs', () => {
  const draft = stage(new Workspace().get('uno'), create('one'), 'initial')
  let state = confirmed(draft, { changeId: 'original' })
  state = history(state, 'undo', 'fresh')
  assert.deepEqual(state.intent, { kind: 'undo', target: 'original' })
  assert.equal(state.version, 'fresh')
  assert.deepEqual(state.frame, [])
  state = confirmed(state, { changeId: 'undo-receipt' })
  state = history(state, 'redo', 'after-undo')
  assert.deepEqual(state.intent, { kind: 'redo', target: 'undo-receipt' })
  state = confirmed(state, { changeId: 'redo-receipt' })
  assert.deepEqual(history(state, 'undo', 'after-redo').intent, { kind: 'undo', target: 'redo-receipt' })
})

test('three-way merge preserves independent fields and rejects overlapping changes', () => {
  const original = { cardId: 'one', tags: ['a'], rarity: 'Common' }
  assert.deepEqual(mergeIndependent(original, { ...original, tags: ['a', 'b'] }, { ...original, rarity: 'Rare' }),
    { cardId: 'one', tags: ['a', 'b'], rarity: 'Rare' })
  assert.equal(mergeIndependent(original, { ...original, tags: ['b'] }, { ...original, tags: ['c'] }), undefined)
})

test('a pending request keeps its history locked until the engine confirms it', () => {
  const state = { ...confirmed(new Workspace().get('uno'), { changeId: 'previous' }),
    attempt: { request: { changeId: 'retry' }, version: 'before' } }
  assert.equal(history(state, 'undo', 'latest'), state)
})
