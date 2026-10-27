const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const source = fs.readFileSync(path.join(__dirname, '..', 'app.js'), 'utf8');
function render(allowed) {
  const context = vm.createContext({
    unoEnergy: 1, wildToken: 0,
    legalSnap: { candidates: allowed ? [{ command: { actionType: 'END_TURN' } }] : [],
      blockedCommands: allowed ? [] : [{ actionType: 'END_TURN', code: 'DRAW_REQUIRED', messageKey: 'uno.draw_required' }] },
    state: { busy: false, canEndTurn: true },
    el: { endTurnBtn: { classList: { toggle() {} } } }
  });
  const start = source.indexOf('    // End Turn button');
  vm.runInContext(source.slice(start, source.indexOf('    if (el.turnOwnerBadge)', start)), context);
  return context;
}
test('the client no longer owns the draw-before-pass rule', () => {
  assert.equal(source.includes('requiresDrawBeforePassing'), false);
});
test('a server rejection disables manual and automatic turn completion', async () => {
  const context = render(false);
  assert.equal(context.state.canEndTurn, false);
  assert.equal(context.el.endTurnBtn.disabled, true);
  assert.match(context.el.endTurnBtn.title, /compre 1 carta/);
  const start = source.indexOf('  async function handleEndTurn()');
  vm.runInContext(source.slice(start, source.indexOf('  function showOutcomeModal(', start)), context);
  await context.handleEndTurn();
});
test('server availability enables the button without local card-count calculations', () => {
  const context = render(true);
  assert.equal(context.state.canEndTurn, true);
  assert.equal(context.el.endTurnBtn.disabled, false);
});
