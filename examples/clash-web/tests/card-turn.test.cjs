const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const root = path.resolve(__dirname, '..', 'content/clash/Resources');
const cards = JSON.parse(fs.readFileSync(path.join(root, 'cards/clash.json'), 'utf8'));
const gambits = JSON.parse(fs.readFileSync(path.join(root, 'gambits/clash.json'), 'utf8'));
test('every played card renews the next card-turn draw and CLASH declaration for its actual actor', () => {
  for (const [id, card] of Object.entries(cards)) {
    for (const resource of ['clash_draw_available', 'clash_voice', 'clash_catch_available']) {
      const renewals = card.components.filter(component => component.type === 'effect' && component.effect.targetResource === resource);
      assert.equal(renewals.length, 1, `${id} renewal for ${resource}`);
      const effect = renewals[0].effect;
      assert.equal(effect.target, 'SELF'); assert.equal(effect.operation, 'SET'); assert.equal(effect.flatValue, 1);
    }
  }
});
test('bot prefers its mandatory purchase over ending without a playable card', () => {
  assert.equal(gambits.clash_draw.action.actionId, 'clash_draw_card');
  assert.ok(gambits.clash_draw.priority > gambits.clash_end.priority);
  for (const rule of Object.values(gambits).filter(rule => rule.action.actionType === 'PLAY_CARD'))
    assert.ok(rule.priority > gambits.clash_draw.priority, 'A playable card should be considered before buying');
});
test('catching missed CLASH preserves the card turn and is limited to once per turn', () => {
  const actions = JSON.parse(fs.readFileSync(path.join(root, 'actions/clash.json'), 'utf8'));
  const catchClash = actions.clash_catch_missed_clash;
  assert.ok(catchClash.costs.costs.some(cost => cost.resourceId === 'clash_catch_available' && cost.amount === 1));
  assert.ok(catchClash.effects.some(effect => effect.target === 'SELF' && effect.targetResource === 'clash_energy' &&
    effect.operation === 'ADD' && effect.flatValue === 1));
});
