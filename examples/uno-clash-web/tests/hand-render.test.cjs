const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const source = fs.readFileSync(path.join(__dirname, '..', 'app.js'), 'utf8');
const renderer = source.slice(source.indexOf('  function renderPlayerHand('), source.indexOf('  async function initSettingAndStartRun('));
class Element {
  constructor() {
    this.children = []; this.dataset = {}; this.attributes = {}; this.events = {};
    const classes = new Set();
    this.classList = { add: name => classes.add(name), remove: name => classes.delete(name),
      contains: name => classes.has(name), toggle: (name, value) => value ? classes.add(name) : classes.delete(name) };
    this.style = { setProperty() {} };
  }
  setAttribute(key, value) { this.attributes[key] = value; }
  addEventListener(name, handler) { this.events[name] = handler; }
  remove() { if (this.parent) this.parent.children.splice(this.parent.children.indexOf(this), 1); this.parent = null; }
  insertBefore(card, next) { card.remove(); const index = next ? this.children.indexOf(next) : this.children.length; this.children.splice(index, 0, card); card.parent = this; }
  replaceChildren() { for (const card of [...this.children]) card.remove(); }
  getBoundingClientRect() { return { left: this.parent ? this.parent.children.indexOf(this) * 60 : 0 }; }
  getAnimations() { return []; }
}
const evaluation = (id, definitionId = 'clash_red_7') => ({ presentation: { identity: { cardInstanceId: id, definitionId } }, evaluation: { failureReasons: [] } });
function setup() {
  let bindings = 0;
  const h = vm.createContext({ state: { runId: 'run', combatId: 'match', legalByCardInstance: new Map() },
    el: { playerHandContainer: new Element() }, parseCardIdentity: id => ({ label: id }),
    createCardElement: ({ extraClass }) => { const card = new Element(); card.classList.add(extraClass); return card; },
    attachCardDragAndClick: () => bindings++ });
  vm.runInContext(renderer, h);
  h.bindings = () => bindings;
  h.cards = () => h.el.playerHandContainer.children;
  h.dealt = () => h.cards().forEach(card => card.events.animationend());
  return h;
}
test('playing removes only that instance and keeps surviving cards without another entrance', () => {
  const h = setup(); h.renderPlayerHand(['a', 'b', 'c'].map(id => evaluation(id)), 0); h.dealt();
  const [a, b, c] = h.cards(); h.renderPlayerHand(['a', 'c'].map(id => evaluation(id)), 0);
  assert.deepEqual(h.cards(), [a, c]); assert.equal(b.parent, null); assert.equal(h.bindings(), 3);
  assert.ok(h.cards().every(card => !card.classList.contains('hand-card-entering')));
});
test('buying animates only the new card, even when its definition duplicates another', () => {
  const h = setup(); h.renderPlayerHand([evaluation('a')], 0); h.dealt(); const a = h.cards()[0];
  h.renderPlayerHand([evaluation('a'), evaluation('b')], 0);
  assert.equal(h.cards()[0], a); assert.ok(!a.classList.contains('hand-card-entering'));
  assert.ok(h.cards()[1].classList.contains('hand-card-entering')); assert.equal(h.bindings(), 2);
});
test('legality and wild selection update existing cards without rebinding interactions', () => {
  const h = setup(); const snapshot = [evaluation('a')]; h.renderPlayerHand(snapshot, 0); const card = h.cards()[0];
  assert.ok(card.classList.contains('is-illegal'));
  h.state.legalByCardInstance.set('a', {}); h.renderPlayerHand(snapshot, 0);
  assert.equal(h.cards()[0], card); assert.ok(card.classList.contains('is-legal'));
  h.renderPlayerHand(snapshot, 1); assert.ok(card.classList.contains('is-legal')); // Server candidates remain the sole legality authority. assert.equal(h.bindings(), 1);
});
test('reordering preserves instances and a new match starts a fresh deal', () => {
  const h = setup(); h.renderPlayerHand(['a', 'b'].map(id => evaluation(id)), 0); const [a, b] = h.cards();
  h.renderPlayerHand(['b', 'a'].map(id => evaluation(id)), 0); assert.deepEqual(h.cards(), [b, a]);
  h.state.combatId = 'next'; h.renderPlayerHand([evaluation('a')], 0);
  assert.notEqual(h.cards()[0], a); assert.ok(h.cards()[0].classList.contains('hand-card-entering'));
});
