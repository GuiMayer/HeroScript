const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const source = fs.readFileSync(path.join(__dirname, '..', 'app.js'), 'utf8');
const functions = source.slice(source.indexOf('  function clearTableFlight()'), source.indexOf('  function startAutoTurnProgress'));
function setup(animate = true) {
  const cards = [], events = [];
  function card(spec) {
    const element = { spec, style: {}, removed: false,
      remove() { this.removed = true; events.push('remove'); },
      getBoundingClientRect() { return { width: parseFloat(this.style.width) || 100, height: parseFloat(this.style.height) || 146 }; }
    };
    if (animate) element.animate = (frames, options) => {
      element.frames = frames; element.options = options; element.animation = {}; return element.animation;
    };
    cards.push(element); return element;
  }
  const target = { left: 500, top: 100, width: 112, height: 164 };
  const context = vm.createContext({
    state: { tableFlightGeneration: 0, pendingTableFlight: null },
    el: { fxLayer: { appendChild() {} }, discardStage: {
      querySelector: () => ({ getBoundingClientRect: () => target }),
      getBoundingClientRect: () => target,
      appendChild() { events.push('append-table'); }
    } },
    document: { createElement: () => card(null) }, createCardElement: card
  });
  vm.runInContext(functions, context);
  const start = { left: 50, top: 300, width: 100, height: 146 };
  return { context, cards, events, play: () => context.animateCardFlight(start, context.el.discardStage, { color: 'blue', corner: '7', center: '7' }, 340) };
}
test('landed card remains visible while the API snapshot is delayed', async () => {
  const h = setup(); const flight = h.play(); const card = h.cards[0];
  card.animation.onfinish(); await flight;
  // The animation has finished, but no server snapshot has been rendered yet.
  await Promise.resolve();
  assert.equal(card.removed, false);
  assert.equal(h.context.state.pendingTableFlight, card);
  assert.equal(card.frames.at(-1).opacity, 1);
  assert.match(card.frames.at(-1).transform, /scale\(1, 1\)/);
  assert.equal(card.style.width, '112px');
  assert.equal(card.style.height, '164px');
});
test('snapshot installs the table card before removing the flight, without a second entrance', async () => {
  const h = setup(); const flight = h.play(); h.cards[0].animation.onfinish(); await flight;
  h.context.decodedTable = { color: 'blue', corner: '7', center: '7', isTextGlyph: false };
  h.context.tableChanged = true;
  const start = source.indexOf('    const tableCardEl = createCardElement(');
  const end = source.indexOf('    el.activeColorLabel.textContent', start);
  vm.runInContext(source.slice(start, end), h.context);
  assert.deepEqual(h.events, ['append-table', 'remove']);
  assert.equal(h.cards[1].spec.extraClass, 'table-card');
  assert.equal(h.context.state.pendingTableFlight, null);
});
test('bot chain keeps the previous landed card until the next one lands', async () => {
  const h = setup(); const first = h.play(); h.cards[0].animation.onfinish(); await first;
  const second = h.play(); assert.equal(h.cards[0].removed, false);
  h.cards[1].animation.onfinish(); await second;
  assert.equal(h.cards[0].removed, true); assert.equal(h.cards[1].removed, false);
});
test('a late animation cannot leave a rejected card on the table', async () => {
  const h = setup(); const flight = h.play(); h.context.clearTableFlight();
  h.cards[0].animation.onfinish(); await flight;
  assert.equal(h.cards[0].removed, true); assert.equal(h.context.state.pendingTableFlight, null);
});
test('fallback without browser animation also holds the landed card', async () => {
  const h = setup(false); await h.play(); assert.equal(h.cards[0].removed, false);
  h.context.clearTableFlight(); assert.equal(h.cards[0].removed, true);
});
test('draw flights still clean up when they reach a hand', async () => {
  const h = setup(); const hand = { getBoundingClientRect: () => ({ left: 400, top: 400, width: 100, height: 146 }) };
  const flight = h.context.animateCardFlight({ left: 0, top: 0, width: 100, height: 146 }, hand);
  h.cards[0].animation.onfinish(); await flight;
  assert.equal(h.cards[0].removed, true); assert.equal(h.context.state.pendingTableFlight, null);
});
