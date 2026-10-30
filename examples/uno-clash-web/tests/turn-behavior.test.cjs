const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const source = fs.readFileSync(path.join(__dirname, '..', 'app.js'), 'utf8');
const helpers = source.slice(source.indexOf('  function cancelAutoTurn()'), source.indexOf('  // Parse card definition ID'));
const autoStart = source.indexOf('    // Optional automatic input schedules');
const autoRules = source.slice(autoStart, source.indexOf('\n  }', autoStart));
function setup() {
  let pending, passes = 0;
  const button = () => ({ disabled: false, classes: new Set(['must-draw-pulse', 'ready-glow', 'urgent']),
    get classList() { return { remove: name => this.classes.delete(name) }; } });
  const context = vm.createContext({
    state: { canEndTurn: true, legalByActionId: new Map(), autoEndTurnEligible: true, autoEndTurnDelay: 680, combatId: 'match', combatStep: 3, busy: false },
    el: { autoEndTurnToggle: { checked: true }, drawCardBtn: button(), endTurnBtn: button(), shoutClashBtn: button(), shoutUnoBtn: button(), drawPileHint: {} },
    clearTimeout(timer) { if (timer) timer.cancelled = true; },
    setTimeout(callback, delay) { pending = { callback, delay }; return pending; },
    resetAutoTurnProgress() {}, startAutoTurnProgress() {}, handleEndTurn() { passes++; },
    actionId: role => role, purchase: null, pendingDraw2: 0, clashEnergy: 1, unoEnergy: 1, drawAvailable: 0, legalCardsCount: 1, playerHandCount: 3, clashCalled: 0, unoCalled: 0, canShoutClash: false, canShoutUno: false
  });
  vm.runInContext(helpers, context);
  return { context, schedule() { context.updateAutoTurn(); }, refresh() { vm.runInContext(autoRules, context); },
    fire() { if (pending && !pending.cancelled) pending.callback(); }, get passes() { return passes; }, get pending() { return pending; } };
}
test('manual pass immediately stops draw hints and disables player controls', () => {
  const h = setup(); h.schedule(); h.context.suspendPlayerControls(); h.fire();
  for (const name of ['drawCardBtn', 'endTurnBtn', 'shoutClashBtn']) assert.equal(h.context.el[name].disabled, true);
  assert.equal(h.context.el.drawCardBtn.classes.has('must-draw-pulse'), false);
  assert.equal(h.passes, 0);
});
test('buying a playable card keeps the turn available without automatic passing', () => {
  const h = setup(); h.refresh(); assert.equal(h.pending, undefined); assert.equal(h.context.state.canEndTurn, true);
});
test('after buying without a compatible card automatic mode can pass', () => {
  const h = setup(); h.context.legalCardsCount = 0; h.refresh(); h.fire(); assert.equal(h.passes, 1);
});
test('manual mode keeps the choice to pass after buying', () => {
  const h = setup(); h.context.el.autoEndTurnToggle.checked = false; h.context.legalCardsCount = 0;
  h.refresh(); assert.equal(h.pending, undefined); assert.equal(h.context.state.canEndTurn, true);
});
test('a pending +2 with a legal response waits for the player', () => {
  const h = setup(); h.context.pendingDraw2 = 4; h.context.drawAvailable = 1;
  h.refresh(); assert.equal(h.pending, undefined);
});
test('a blocked pass never invents a penalty acceptance command', () => {
  const h = setup(); h.context.state.canEndTurn = false; h.context.purchase = {}; h.context.legalCardsCount = 0;
  h.refresh(); h.fire(); assert.equal(h.passes, 0);
});
test('manual pass submits the exact legal candidate returned by the server', async () => {
  const h = setup(); const requests = [];
  h.context.state.endTurnCandidate = { command: { actorId: 'seat_0', actionType: 'END_TURN' } };
  Object.assign(h.context, { uuid: () => 'cmd', toCombatPayload: v => v,
    api: async (method, route, body) => { requests.push(body.payload); return {}; },
    animateOpponentEvents: async () => {}, refreshArenaState: async () => {}, appendLog() {}, clearTableFlight() {}, setBannerMessage() {} });
  const handler = source.slice(source.indexOf('  async function handleEndTurn()'), source.indexOf('  function showOutcomeModal('));
  vm.runInContext(handler, h.context); await h.context.handleEndTurn();
  assert.deepEqual(requests, [{ actorId: 'seat_0', actionType: 'END_TURN' }]);
});

for (const [name, change] of [
  ['automatic mode disabled', c => { c.el.autoEndTurnToggle.checked = false; }],
  ['pass no longer legal', c => { c.state.canEndTurn = false; }],
  ['another play available', c => { c.state.autoEndTurnEligible = false; }],
  ['combat changed', c => { c.state.combatId = 'other'; }],
  ['step changed', c => { c.state.combatStep++; }],
  ['action in progress', c => { c.state.busy = true; }]
]) test(`pending automatic pass does not fire when ${name}`, () => {
  const h = setup(); h.schedule(); change(h.context); h.fire(); assert.equal(h.passes, 0);
});
test('refresh cancels the old timer when buying creates a playable hand', () => {
  const h = setup(); h.schedule(); const old = h.pending; h.refresh(); assert.equal(old.cancelled, true); h.fire(); assert.equal(h.passes, 0);
});
test('both entry points call the same handler and lock lasts until refresh finishes', () => {
  assert.match(source, /el\.endTurnBtn\.addEventListener\("click", \(\) => handleEndTurn\(\)\)/);
  const handler = source.slice(source.indexOf('  async function handleEndTurn()'), source.indexOf('  function showOutcomeModal('));
  assert.match(handler, /suspendPlayerControls\(\)/);
  assert.doesNotMatch(handler, /state\.busy = false;\s*await refreshArenaState/);
});
