const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const modes = require('../game-modes.js');
const source = fs.readFileSync(path.join(__dirname, '..', 'app.js'), 'utf8');
const parser = source.slice(source.indexOf('  function parseCardIdentity('), source.indexOf('  function createCardElement('));
test('both card namespaces share their visual presentation without translating commands', () => {
  const context = vm.createContext({ COLOR_LABELS: { red: 'Vermelho', blue: 'Azul' } });
  vm.runInContext(parser, context);
  for (const [duel, table] of [['clash_red_reverse', 't_red_reverse'], ['clash_blue_7', 't_blue_7'], ['clash_wild', 't_wild_wild'], ['clash_wild_draw4', 't_wild_draw4']]) {
    assert.deepEqual(context.parseCardIdentity(duel), context.parseCardIdentity(table));
  }
  const wild = context.parseCardIdentity('clash_wild');
  assert.equal(wild.corner, '★');
  assert.equal(wild.center, '★');
  assert.equal(wild.isWild, true);
  assert.equal(wild.isTextGlyph, false);
});
test('opponent presentation preserves each seat identity and excludes the human actor', () => {
  const context = vm.createContext({ playerId: () => 'seat_0' });
  vm.runInContext(source.slice(source.indexOf('  function movedCardsToHands('), source.indexOf('  async function withBotActionPacing(')), context);
  const frames = ['seat_0', 'seat_1', 'seat_2', 'seat_3'].map(actorId => ({ payload: { command: { actorId, actionType: 'PLAY_CARD' }, cardResolution: { definitionId: 't_red_7' } } }));
  const events = context.opponentEvents({ state: { resolution: { frames } } });
  assert.deepEqual(Array.from(events, event => event.actorId), ['seat_1', 'seat_2', 'seat_3']);
});
test('recycling visuals read recorded physical movements and never include the table zone', () => {
  const context = vm.createContext({});
  vm.runInContext(source.slice(source.indexOf('  function pileSnapshot('), source.indexOf('  function presentPileSnapshot(')), context);
  const snapshot = context.pileSnapshot({ abilityResolution: { applications: [{ CardZoneSteps: [
    { SourceAddress: '$run::$run::discard', TargetAddress: '$run::$run::draw', SourceOrderAfter: [], TargetOrderAfter: ['a', 'b', 'c'] },
    { SourceAddress: '$run::$run::draw', TargetAddress: '$run::seat_2::hand', SourceOrderAfter: ['b', 'c'], TargetOrderAfter: ['old', 'a'] }
  ] }] } });
  assert.equal(snapshot.count, 2); assert.equal(snapshot.recycled, true);
});
test('empty, thin and full piles reflect API counts and retain their layout nodes', () => {
  const layers = Array.from({ length: 4 }, () => ({ hidden: false, classList: { toggle(name, value) { this.owner.hidden = value; } } }));
  layers.forEach(layer => layer.classList.owner = layer);
  const button = { dataset: {}, querySelectorAll: () => layers, setAttribute() {}, classList: { toggle() {} } };
  const context = vm.createContext({ state: { deckCapacity: 108 }, el: { drawCardBtn: button } });
  vm.runInContext(source.slice(source.indexOf('  function renderDrawPile('), source.indexOf('  function renderPlayerHand(')), context);
  for (const [count, visible] of [[null, 4], [108, 4], [1, 1], [0, 0], [80, 3]]) {
    context.renderDrawPile(count); assert.equal(layers.filter(layer => !layer.hidden).length, visible);
    assert.equal(button.querySelectorAll(), layers);
  }
});
test('mode bindings contain presentation and content identifiers, not game simulations', () => {
  assert.equal(modes.duel.settingId, 'clash'); assert.equal(modes.freeForAll.settingId, 'circular-table');
  assert.deepEqual(Object.values(modes.freeForAll.seats), ['seat_1', 'seat_2', 'seat_3']);
  assert.doesNotMatch(source, /requiresDrawBeforePassing|new LocalUnoGame|\.pendingDraw2\s*[><=]/);
  assert.match(source, /payload: toCombatPayload\(state\.endTurnCandidate\.command\)/);
  assert.doesNotMatch(source.slice(source.lastIndexOf('// No run is created')), /initSettingAndStartRun\(/);
});

test('orbit forward rotation follows the counterclockwise visual seat order', () => {
  const css = fs.readFileSync(path.join(__dirname, '..', 'styles.css'), 'utf8');
  const spin = css.slice(css.indexOf('@keyframes orbitSpin'), css.indexOf('.discard-stage {', css.indexOf('@keyframes orbitSpin')));
  assert.match(spin, /rotate\(-360deg\)/);
  assert.match(css, /\.color-orbit-ring\.reversed\s*\{\s*animation-direction:\s*reverse/);
  assert.equal(modes.freeForAll.seats.right, 'seat_1');
  assert.equal(modes.freeForAll.seats.top, 'seat_2');
  assert.equal(modes.freeForAll.seats.left, 'seat_3');
});
