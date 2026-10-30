const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const source = fs.readFileSync(path.join(__dirname, '..', 'app.js'), 'utf8');
const renderer = source.slice(source.indexOf('  function renderBotHandTelemetry()'), source.indexOf('  async function initSettingAndStartRun('));
const parser = source.slice(source.indexOf('  function parseCardIdentity('), source.indexOf('  function createCardElement('));
class Element {
  constructor() { this.children = []; this.dataset = {}; this.attributes = {}; this.hidden = false;
    this.classList = { toggle: (name, value) => { if (name === 'hidden') this.hidden = value; } }; }
  replaceChildren() { this.children = []; }
  append(...items) { this.children.push(...items); }
  appendChild(item) { this.children.push(item); }
  setAttribute(key, value) { this.attributes[key] = value; }
}
function setup() {
  const context = vm.createContext({
    state: { combatId: 'match', botHandCount: 3, botHandCards: [
      { cardInstanceId: 'red-a', definitionId: 'clash_red_7' }, { cardInstanceId: 'red-b', definitionId: 'clash_red_7' },
      { cardInstanceId: 'wild', definitionId: 'clash_wild_draw4' }
    ] },
    el: { showBotCardsToggle: { checked: false }, telemetryBotCards: new Element(), telemetryBotCardsStatus: {} },
    actorLabel: () => 'Bot', COLOR_LABELS: { red: 'Vermelho' }, document: { createElement: () => new Element() },
    createCardElement: parsed => Object.assign(new Element(), { parsed })
  });
  context.state.botHands = { dealer: { count: context.state.botHandCount, cards: context.state.botHandCards } };
  Object.defineProperties(context.state, {
    botHandCount: { get: () => context.state.botHands.dealer.count, set: v => context.state.botHands.dealer.count = v },
    botHandCards: { get: () => context.state.botHands.dealer.cards, set: v => context.state.botHands.dealer.cards = v }
  });
  vm.runInContext(parser + renderer, context); return context;
}
test('bot cards stay hidden by default in telemetry', () => {
  const h = setup(); h.renderBotHandTelemetry();
  assert.equal(h.el.telemetryBotCards.hidden, true); assert.equal(h.el.telemetryBotCards.children.length, 0);
});
test('enabled telemetry shows every actual card, including duplicate definitions', () => {
  const h = setup(); h.el.showBotCardsToggle.checked = true; h.renderBotHandTelemetry();
  assert.equal(h.el.telemetryBotCards.hidden, false);
  assert.deepEqual(h.el.telemetryBotCards.children.map(item => item.dataset.cardInstanceId), ['red-a', 'red-b', 'wild']);
  assert.deepEqual(h.el.telemetryBotCards.children.map(item => item.children[1].textContent), ['Bot · 7 Vermelho', 'Bot · 7 Vermelho', 'Bot · Coringa +4']);
});
test('disabling the option removes previously displayed cards', () => {
  const h = setup(); h.el.showBotCardsToggle.checked = true; h.renderBotHandTelemetry();
  h.el.showBotCardsToggle.checked = false; h.renderBotHandTelemetry();
  assert.equal(h.el.telemetryBotCards.children.length, 0); assert.equal(h.el.telemetryBotCards.hidden, true);
});
test('telemetry refresh replaces played cards and shows the new hand count', () => {
  const h = setup(); h.el.showBotCardsToggle.checked = true; h.renderBotHandTelemetry();
  h.state.botHandCards = [{ cardInstanceId: 'new', definitionId: 'clash_red_2' }]; h.state.botHandCount = 1;
  h.renderBotHandTelemetry(); assert.equal(h.el.telemetryBotCards.children.length, 1);
  assert.equal(h.el.telemetryBotCards.children[0].dataset.cardInstanceId, 'new');
  assert.equal(h.el.telemetryBotCards.children.length, 1);
});
test('an old private revision gives an explanation instead of invented cards', () => {
  const h = setup(); h.state.botHandCards = []; h.el.showBotCardsToggle.checked = true; h.renderBotHandTelemetry();
  assert.equal(h.el.telemetryBotCards.children.length, 1); assert.match(h.el.telemetryBotCards.children[0].textContent, /ocultas/);
});
test('game JSON enables telemetry reads while keeping actor-owned hands and a hidden draw pile', () => {
  const zones = JSON.parse(fs.readFileSync(path.resolve(__dirname, '../content/clash/Resources/card-zone-systems/clash.json'), 'utf8')).clash_zones.zones;
  const hand = zones.find(zone => zone.zoneId === 'hand');
  assert.equal(hand.ownerScope, 'Actor'); assert.equal(hand.visibility.contents, 'All');
  assert.equal(zones.find(zone => zone.zoneId === 'draw').visibility.contents, 'None');
});
