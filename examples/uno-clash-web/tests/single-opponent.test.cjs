const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const root = path.resolve(__dirname, '../content/clash/Resources');
const load = kind => JSON.parse(fs.readFileSync(path.join(root, kind, 'clash.json'), 'utf8'));
test('game authors one terminal table with one generic bot, preserving the physical deck', () => {
  const entities = load('entities'), run = load('runs').clash_run;
  assert.deepEqual(Object.keys(entities).sort(), ['clash_bot', 'clash_player']);
  assert.deepEqual(load('enemy-pools').clash_opponents.entityDefinitionIds, ['clash_bot']);
  assert.equal(run.mapNodes.length, 1);
  assert.equal(run.mapNodes[0].activity.parameters.participants.find(actor => actor.instanceId === 'dealer').definitionId, 'clash_bot');
  assert.deepEqual(run.mapNodes[0].nextNodeIds, []);
  assert.equal(run.startingCards.length, 108);
});
test('victory and defeat both offer a new match without a next enemy or tournament', () => {
  const source = fs.readFileSync(path.join(__dirname, '..', 'app.js'), 'utf8');
  const element = () => ({ classList: { add() {}, remove() {} } });
  const el = Object.fromEntries(['wildModal', 'outcomeModal', 'outcomeBadge', 'outcomeTitle', 'outcomeDescription'].map(id => [id, element()]));
  const context = vm.createContext({ el, actorLabel: () => "Bot", suspendPlayerControls() {}, showCalloutSplash() {} });
  const start = source.indexOf('  function showOutcomeModal(');
  vm.runInContext(source.slice(start, source.indexOf('  // =========================================================================', start)), context);
  for (const status of ['VICTORY', 'DEFEAT']) {
    context.showOutcomeModal(status);
    assert.match(el.outcomeDescription.textContent, /nova partida/i);
    assert.doesNotMatch(el.outcomeDescription.textContent, /copa|rei|condessa|barão|próximo/i);
  }
});
