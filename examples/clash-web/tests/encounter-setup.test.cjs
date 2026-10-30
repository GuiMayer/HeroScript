const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const source = fs.readFileSync(path.join(__dirname, '..', 'app.js'), 'utf8');
test('new encounter stays locked through real dealing and the complete first refresh', async () => {
  let finishRefresh, reachedRefresh = false;
  const refresh = new Promise(resolve => { finishRefresh = resolve; });
  const state = { runId: 'run', busy: false };
  const busyDuringRequests = [];
  const context = vm.createContext({ state, suspendPlayerControls() { assert.equal(state.busy, true); }, clearTableFlight() {},
    actionId: role => role === 'setup' ? 'clash_setup_table' : role, uuid: () => 'command', toCombatPayload: value => value, appendLog() {}, setBannerMessage() {},
    api: async (method, url) => {
      busyDuringRequests.push(state.busy);
      if (url.endsWith('/available-commands')) return { commands: [{ type: 'START_ENCOUNTER', expectedSequence: 1, expectedStep: 0, validPayload: {} }] };
      if (url.endsWith('/legal-actions')) return { candidates: [{ actionId: 'clash_setup_table', command: { powerId: 'clash_setup_table' } }] };
      if (method === 'POST' && url.includes('/runs/')) return { state: { activeEncounterId: 'combat', currentNodeId: 'table' }, sequence: 2 };
      return { sequence: 2, step: 0 };
    }, refreshArenaState: async () => { reachedRefresh = true; await refresh; }
  });
  const start = source.indexOf('  async function startCurrentNodeEncounter()');
  vm.runInContext(source.slice(start, source.indexOf('  async function refreshArenaState()', start)), context);
  const pending = context.startCurrentNodeEncounter();
  for (let turn = 0; turn < 20 && !reachedRefresh; turn++) await Promise.resolve();
  assert.equal(reachedRefresh, true); assert.equal(state.busy, true);
  assert.ok(busyDuringRequests.length >= 5 && busyDuringRequests.every(Boolean));
  finishRefresh(); await pending; assert.equal(state.busy, false);
});
