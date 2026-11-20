// End-to-end test of all HeroScript tools over actual MCP stdio against a dedicated sandbox.
// Usage: node tools/test-heroscript-mcp.mjs <HeroScript.Mcp.dll> <sandbox origin>
import { spawn } from 'node:child_process';
import { createInterface } from 'node:readline';
import assert from 'node:assert/strict';

const [dll, origin] = process.argv.slice(2);
assert(dll && origin, 'Specify MCP DLL and isolated sandbox API origin');
const marker = await fetch(`${origin}/api/v1/diagnostics/mcp-sandbox`);
assert(marker.ok && (await marker.json()).isolated, 'Refusing to test against a non-sandbox API');
const child = spawn('dotnet', [dll], { stdio: ['pipe', 'pipe', 'inherit'], env: {
  ...process.env, HEROSCRIPT_MCP_API_URL: origin, HEROSCRIPT_MCP_SANDBOX_URL: origin,
  Logging__LogLevel__Default: 'Warning'
} });
let id = 0;
const pending = new Map();
createInterface({ input: child.stdout }).on('line', line => {
  const msg = JSON.parse(line);
  const p = pending.get(msg.id);
  if (!p) return;
  pending.delete(msg.id); clearTimeout(p.timer);
  msg.error ? p.reject(Error(JSON.stringify(msg.error))) : p.resolve(msg.result);
});
function request(method, params = {}) {
  return new Promise((resolve, reject) => {
    const key = ++id;
    const timer = setTimeout(() => { pending.delete(key); reject(Error(`Timeout: ${method}`)); }, 180000);
    pending.set(key, { resolve, reject, timer });
    child.stdin.write(JSON.stringify({ jsonrpc: '2.0', id: key, method, params }) + '\n');
  });
}
const seen = new Set();
async function call(name, args = {}) {
  const res = await request('tools/call', { name, arguments: args });
  assert(!res.isError, `${name}: ${JSON.stringify(res.content)}`);
  seen.add(name); console.log(`PASS ${name}`);
  return res.structuredContent ?? JSON.parse(res.content.find(c => c.type === 'text').text);
}
try {
  const init = await request('initialize', { protocolVersion: '2025-06-18', capabilities: {}, clientInfo: { name: 'heroscript-e2e', version: '1' } });
  console.log('Server', init.serverInfo);
  child.stdin.write(JSON.stringify({ jsonrpc: '2.0', method: 'notifications/initialized' }) + '\n');
  const { tools } = await request('tools/list');
  assert.equal(tools.length, 14);
  const status = await call('get_engine_status'); assert(status.available);
  const settings = await call('list_settings');
  const revision = settings.items.find(i => i.settingId === 'default').currentRevision;
  const alisyum = settings.items.find(i => i.settingId === 'alisyum').currentRevision;
  const validation = await call('validate_content', { settingId: 'default', revision }); assert(validation.validation.isValid);
  const coverage = await call('validate_gambits', { settingId: 'alisyum', revision: alisyum, budget: 3000, runDefinitionIds: ['alisyum_chapter_1','alisyum_servo_run','alisyum_gambler_run','alisyum_renegado_run'] });
  assert.equal(coverage.bindings, 28); assert.equal(coverage.checkedCases, 2016); assert(!coverage.incomplete);
  // A diagnostic must work on a deliberately broken or older revision too. Do not require the content to be fixed.
  if (process.env.HEROSCRIPT_MCP_REQUIRE_CLEAN_GAMBITS === '1') assert.equal(coverage.failedCases, 0);
  console.log('Coverage', coverage.bindings, coverage.checkedCases, coverage.failedCases, coverage.incomplete);
  const scenario = { schemaVersion: 2, modeId: 'combat_sandbox', participants: [
    { instanceId: 'hero', entityDefinitionId: 'player_warrior', sideId: 'player', controllerBinding: { kind: 'Player' } },
    { instanceId: 'goblin_a', entityDefinitionId: 'enemy_goblin', sideId: 'opposition', controllerBinding: { kind: 'AI', policyId: 'gambit' } }
  ], startingCards: ['basic_attack', 'basic_attack', 'defend', 'fireball', 'heal'].map(definitionId => ({ definitionId })) };
  const encounter = await call('simulate_encounter', { settingId: 'default', revision, seed: 983744, scenario, maxCommands: 1 });
  assert(encounter.actions.length > 0 && !encounter.error);
  const { runId, combatId } = encounter;
  const run = await call('inspect_run', { runId });
  const { state, legalActions } = await call('inspect_combat', { combatId, actorId: 'hero' });
  assert.equal(state.combatId, combatId);
  const entity = await call('inspect_entity', { combatId, actorId: 'hero' }); assert.equal(entity.actor.instanceId, 'hero');
  const picked = legalActions.candidates.find(c => c.command.actionType === 'PLAY_CARD');
  assert(picked, 'Need a playable card to inspect');
  await call('inspect_card', { combatId, cardInstanceId: picked.command.cardInstanceId, actorId: 'hero', targetIds: picked.command.targetIds });
  const gambits = await call('inspect_gambits', { runId, combatId, actorId: 'goblin_a' }); assert.equal(gambits.actorId, 'goblin_a'); assert(gambits.success && gambits.trace.some(t => t.outcome === 'selected'));
  const save = await call('inspect_save', { runId }); assert.equal(save.contentRevision, revision);
  const verified = await call('verify_replay', { runId }); assert(verified.verification.isValid && verified.stateUnchanged);
  const branch = await call('simulate_branch', { runId, sequence: run.run.sequence, commands: [] }); assert(branch.sourceUnchanged);
  const balance = await call('analyze_cards', { settingId: 'default', revision, scenario, seeds: [11,12], cardDefinitionIds: ['basic_attack', 'defend'], maxCommands: 1 });
  assert.equal(balance.samples, 2); assert.equal(balance.failures.length, 0);
  const after = await call('inspect_run', { runId }); assert.equal(after.run.stateHash, run.run.stateHash);
  assert.deepEqual([...seen].sort(), tools.map(t => t.name).sort());
  console.log(`PASS all ${seen.size} tools; run ${runId}; combat ${combatId}; original sandbox source unchanged`);
} finally { child.kill(); }
