const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const modes = require('../game-modes.js');
const source = fs.readFileSync(path.join(__dirname, '..', 'app.js'), 'utf8');

function render(mode, responseCount, penalty) {
  const accept = { actionId: mode.actions.accept, command: { actionType: 'POWER', powerId: mode.actions.accept } };
  const draw = { actionId: mode.actions.draw, command: { actionType: 'POWER', powerId: mode.actions.draw } };
  const end = { command: { actionType: 'END_TURN' } };
  const classes = new Set(['must-draw-pulse']);
  const state = { legalByCardInstance: new Map(Array.from({ length: responseCount }, (_, i) => [i, {}])),
    legalByActionId: new Map(penalty ? [[mode.actions.accept, accept]] : [[mode.actions.draw, draw]]) };
  const context = vm.createContext({ state, actionId: role => mode.actions[role],
    legalSnap: { candidates: penalty ? [accept] : [draw, end], blockedCommands: penalty ? [{ actionType: 'END_TURN', code: 'DEBT_REQUIRED' }] : [] },
    el: { drawCardBtn: { classList: { toggle(name, on) { on ? classes.add(name) : classes.delete(name); } } },
      shoutClashBtn: { classList: { toggle() {} } }, endTurnBtn: {} } });
  const start = source.indexOf('    const legalCardsCount =');
  vm.runInContext(source.slice(start, source.indexOf('    el.turnOwnerBadge.textContent', start)), context);
  return { context, classes, accept, end };
}

for (const mode of Object.values(modes)) {
  test(`${mode.label}: a legal +2 response moves penalty acceptance to Pass and removes the pile hint`, () => {
    const h = render(mode, 1, true);
    assert.equal(h.context.el.drawCardBtn.disabled, true);
    assert.equal(h.classes.has('must-draw-pulse'), false);
    assert.equal(h.context.el.endTurnBtn.disabled, false);
    assert.equal(h.context.state.endTurnCandidate, h.accept);
  });
  test(`${mode.label}: without a response the pile can still accept the penalty`, () => {
    const h = render(mode, 0, true);
    assert.equal(h.context.el.drawCardBtn.disabled, false);
    assert.equal(h.context.state.endTurnCandidate, h.accept);
  });
  test(`${mode.label}: ordinary draw and pass keep their original API commands`, () => {
    const h = render(mode, 1, false);
    assert.equal(h.context.el.drawCardBtn.disabled, false);
    assert.equal(h.context.state.endTurnCandidate, h.end);
  });
  test(`${mode.label}: Pass executes the legal penalty action through the existing purchase presentation`, async () => {
    const h = render(mode, 1, true), calls = [];
    h.context.handleExecuteAction = async (...args) => { calls.push(args); };
    const start = source.indexOf('  async function handleEndTurn()');
    vm.runInContext(source.slice(start, source.indexOf('  function showOutcomeModal(', start)), h.context);
    await h.context.handleEndTurn();
    assert.equal(calls.length, 1);
    assert.equal(calls[0][0], mode.actions.accept);
  });
  test(`${mode.label}: one Pass click accepts and submits the freshly authorized end command`, async () => {
    const h = render(mode, 1, true), requests = [];
    h.context.handleExecuteAction = async () => {
      h.context.state.endTurnCandidate = h.end;
      h.context.state.runSequence = 12;
      h.context.state.combatStep = 4;
    };
    Object.assign(h.context, { suspendPlayerControls() {}, uuid: () => 'cmd', toCombatPayload: v => v,
      api: async (method, route, request) => { requests.push(request); return {}; },
      animateOpponentEvents: async () => {}, refreshArenaState: async () => {}, clearTableFlight() {}, setBannerMessage() {} });
    const start = source.indexOf('  async function handleEndTurn()');
    vm.runInContext(source.slice(start, source.indexOf('  function showOutcomeModal(', start)), h.context);
    await h.context.handleEndTurn();
    assert.equal(requests.length, 1);
    assert.equal(requests[0].payload, h.end.command);
    assert.equal(requests[0].expectedSequence, 12);
    assert.equal(requests[0].expectedStep, 4);
  });
  test(`${mode.label}: accumulated splash uses authoritative application values and leaves +4 separate`, () => {
    const context = vm.createContext({ modeConfig: () => mode });
    const start = source.indexOf('  function cardEffectSplash(');
    vm.runInContext(source.slice(start, source.indexOf('  function opponentEvents(', start)), context);
    for (const value of [2, 4, 6, 8]) {
      const payload = { cardResolution: { applications: [
        { ResourceId: mode.resources.debt, CurrentValue: value },
        { ResourceId: mode.resources.debt, CurrentValue: 0 },
        { ResourceId: 'unrelated', CurrentValue: 100 }
      ] } };
      assert.equal(context.cardEffectSplash({ corner: '+2', specialSplash: 'fallback' }, payload), `💥 COMPRA +${value}!`);
      assert.equal(context.cardEffectSplash({ corner: '+4', specialSplash: '+4' }, payload), '+4');
    }
    assert.equal(context.cardEffectSplash({ corner: '+2' }, { cardResolution: { applications: [
      { resourceId: mode.resources.debt, currentValue: 12 }
    ] } }), '💥 COMPRA +12!');
  });
}
