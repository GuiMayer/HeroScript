const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const source = fs.readFileSync(path.join(__dirname, '..', 'app.js'), 'utf8');
const helpers = source.slice(source.indexOf('  function movedCardsToHands('), source.indexOf('  const state ='));
const parser = source.slice(source.indexOf('  function parseCardIdentity('), source.indexOf('  function createCardElement('));
function setup(frames, options = {}) {
  const flights = [], banners = [], pauses = [], bannerTimes = [], pendingTimers = [], handUpdates = [], playerUpdates = [];
  let clock = 0;
  const state = { busy: false, botHandCount: 7, botHandCards: [{ cardInstanceId: 'physical-card' }], canEndTurn: true, pendingDraw2: 0, orbitReversed: false, combatId: 'combat', runSequence: 3, combatStep: 1, endTurnCandidate: { command: { actorId: 'player', actionType: 'END_TURN' } } };
  state.botHands = { dealer: { count: state.botHandCount, cards: state.botHandCards } };
  Object.defineProperties(state, { botHandCount: { get: () => state.botHands.dealer.count }, botHandCards: { get: () => state.botHands.dealer.cards } });
  const element = { getBoundingClientRect: () => ({}) };
  const context = vm.createContext({ state, COLOR_LABELS: { red: 'Vermelho', blue: 'Azul', green: 'Verde', yellow: 'Amarelo' },
    el: { aiHandFan: element, drawPileAnchor: element, playerHandContainer: {}, discardStage: {}, turnOwnerBadge: {}, colorOrbitRing: { classList: { toggle() {} } } },
    presentPileSnapshot() {}, playerId: () => 'player', actorLabel: () => 'Bot', fanFor: () => element,
    modeConfig: () => ({ resources: { debt: 'clash_draw2_pending' } }),
    toCombatPayload: v => v,
    actionId: role => ({ draw: 'clash_draw_card', accept: 'clash_accept_draw2', call: 'clash_shout_clash', catch: 'clash_catch_missed_clash', choose: 'clash_choose_' })[role],
    performance: { now: () => clock },
    setTimeout(resolve, delay) {
      pauses.push(delay);
      if (options.deferPauses) pendingTimers.push(() => { clock += delay; resolve(); });
      else { clock += delay; resolve(); }
    },
    renderBotHandFan() { handUpdates.push({ time: clock, count: state.botHandCount }); }, renderBotHandTelemetry() {},
    suspendPlayerControls() {}, clearTableFlight() {}, uuid: () => 'command',
    api: async (method, route) => method === 'GET' ? route.endsWith('/card-zones')
      ? { zones: options.finalZones || [] } : { cards: options.finalCards || [] } : ({ state: { resolution: { frames } } }),
    renderPlayerHand(cards) { playerUpdates.push({ time: clock, ids: Array.from(cards, card => card.presentation.identity.cardInstanceId) }); },
    animateCardFlight: async (from, to, card, duration) => { flights.push(card ? `${card.corner}:${card.color}` : to === context.el.playerHandContainer ? 'draw:player' : 'draw:dealer'); clock += options.animationMs ?? duration; },
    spawnShockwave() {}, showCalloutSplash() {}, appendLog() {}, setBannerMessage: text => { banners.push(text); bannerTimes.push(clock); }, refreshArenaState: async () => {}
  });
  vm.runInContext(parser + helpers, context);
  vm.runInContext(source.slice(source.indexOf('  async function handleEndTurn()'), source.indexOf('  function showOutcomeModal(')), context);
  return { context, flights, banners, state, pauses, bannerTimes, pendingTimers, handUpdates, playerUpdates, get clock() { return clock; } };
}
const cardFrame = (definitionId, applications = []) => ({ payload: { resolvedCommand: { ActorId: 'dealer', ActionType: 'PLAY_CARD', CardInstanceId: 'physical-card' }, cardResolution: { definitionId, applications } } });
const moved = (actor, ids) => ({ CardZoneSteps: [{ TargetAddress: `$run::${actor}::hand`, InstanceIds: ids }] });

test('four-player penalty acceptance updates the receiving bot hand and real telemetry before another seat acts', async () => {
  const old = Array.from({ length: 6 }, (_, index) => ({ cardInstanceId: 'old-' + index, definitionId: 't_blue_1' }));
  const received = ['new-a', 'new-b'].map(id => ({ cardInstanceId: id, definitionId: 't_yellow_2' }));
  const after = [...old, ...received];
  const frames = [{ payload: { command: { ActorId: 'seat_3', PowerId: 't_accept' }, abilityResolution: { applications: [{
    CardZoneSteps: [{ TargetAddress: '$run::seat_3::hand', InstanceIds: received.map(card => card.cardInstanceId), TargetOrderAfter: after.map(card => card.cardInstanceId) }]
  }] } } }, { payload: { command: { ActorId: 'seat_1', ActionType: 'PLAY_CARD', CardInstanceId: 'reverse' }, cardResolution: { definitionId: 't_blue_reverse', applications: [] } } }];
  const h = setup(frames, { finalZones: [{ zoneId: 'hand', ownerId: 'seat_3', cards: after }] });
  h.context.playerId = () => 'seat_0';
  h.context.actionId = role => ({ accept: 't_accept', call: 't_call', choose: 't_choose_' })[role];
  h.state.botHands.seat_3 = { count: 6, cards: old };
  const telemetry = [];
  h.context.renderBotHandTelemetry = () => telemetry.push({ time: h.clock, count: h.state.botHands.seat_3.count, ids: Array.from(h.state.botHands.seat_3.cards, card => card.cardInstanceId) });
  await h.context.handleEndTurn();
  assert.deepEqual(telemetry[0], { time: 360, count: 8, ids: after.map(card => card.cardInstanceId) });
  assert.equal(h.bannerTimes[1], 1000);
});

test('consecutive +4 effects update the physical player hand at each landing before the next bot action', async () => {
  const evaluation = id => ({ presentation: { identity: { cardInstanceId: id, definitionId: 'clash_blue_1' } } });
  const first = ['old', 'a', 'b', 'c', 'd'], second = [...first, 'e', 'f', 'g', 'h'];
  const movement = (ids, after) => ({ CardZoneSteps: [{ TargetAddress: '$run::player::hand', InstanceIds: ids, TargetOrderAfter: after }] });
  const h = setup([cardFrame('clash_wild_draw4', [movement(first.slice(1), first)]),
    cardFrame('clash_wild_draw4', [movement(second.slice(5), second)]), cardFrame('clash_red_7')],
    { finalCards: second.map(evaluation) });
  h.state.playerCards = [evaluation('old')];
  await h.context.handleEndTurn();
  assert.deepEqual(h.playerUpdates, [{ time: 740, ids: first }, { time: 1740, ids: second }]);
  assert.equal(h.bannerTimes[1], 1000);
  assert.equal(h.bannerTimes[2], 2000);
});
test('real bot card identities animate in order; stacked +2 does not draw immediately', async () => {
  const h = setup([cardFrame('clash_red_reverse'), cardFrame('clash_red_draw2')]);
  await h.context.handleEndTurn();
  assert.deepEqual(h.flights, ['⇄:red', '+2:red']);
  assert.equal(h.state.orbitReversed, true); assert.equal(h.state.busy, false);
});
test('real +4 plays the card and shows the actual movement into the player hand', async () => {
  const h = setup([cardFrame('clash_wild_draw4', [moved('player', ['a', 'b', 'c', 'd'])])]);
  await h.context.handleEndTurn(); assert.deepEqual(h.flights, ['+4:wild', 'draw:player']);
  assert.ok(h.banners.some(text => text.includes('4 carta(s)')));
});
test('real draw and immediate playable card are both shown', async () => {
  const h = setup([{ payload: { resolvedCommand: { actorId: 'dealer', powerId: 'clash_draw_card' }, abilityResolution: { applications: [moved('dealer', ['drawn'])] } } }, cardFrame('clash_blue_4')]);
  await h.context.handleEndTurn(); assert.deepEqual(h.flights, ['draw:dealer', '4:blue']);
});
test('empty shared pile never produces a phantom draw animation', async () => {
  const h = setup([{ payload: { resolvedCommand: { actorId: 'dealer', powerId: 'clash_draw_card' }, abilityResolution: { applications: [] } } }]);
  await h.context.handleEndTurn(); assert.deepEqual(h.flights, []);
});
test('opponent parser ignores player frames and preserves the bot pass for visual pacing', () => {
  const h = setup([]); const events = h.context.opponentEvents({ state: { resolution: { frames: [
    { payload: { command: { ActorId: 'player' }, cardResolution: { definitionId: 'clash_red_9' } } },
    { payload: { command: { ActorId: 'dealer', ActionType: 'END_TURN' } } }
  ] } } }); assert.equal(events.length, 1); assert.equal(events[0].kind, 'pass');
});


test('each bot card gets at least one second including its flight', async () => {
  const h = setup([cardFrame('clash_red_reverse'), cardFrame('clash_red_7')]);
  await h.context.handleEndTurn();
  assert.deepEqual(h.bannerTimes, [0, 1000]); assert.deepEqual(h.pauses, [620, 620]);
  assert.equal(h.clock, 2000);
});

test('bot hand loses the played card when its flight lands, before the remaining pacing pause', async () => {
  const h = setup([cardFrame('clash_red_7')], { deferPauses: true });
  const pending = h.context.handleEndTurn();
  for (let i = 0; i < 20 && !h.pendingTimers.length; i++) await Promise.resolve();
  assert.deepEqual(h.handUpdates, [{ time: 380, count: 6 }]);
  assert.equal(h.state.botHandCards.length, 0);
  assert.equal(h.state.busy, true);
  h.pendingTimers.shift()(); await pending;
});

test('enlarged floating hand launches a card from one card rather than the entire hand area', async () => {
  const h = setup([cardFrame('clash_red_7')]);
  const rect = { left: 400, top: 130, width: 124, height: 181 };
  h.context.el.aiHandFan.lastElementChild = { getBoundingClientRect: () => rect };
  let origin;
  h.context.animateCardFlight = async from => { origin = from; };
  await h.context.handleEndTurn();
  assert.equal(origin, rect);
});

test('both side hands launch upright cards using their projected dimensions', async () => {
  for (const seat of ['left', 'right']) {
    const h = setup([cardFrame('clash_red_7')]);
    const rect = { left: 40, top: 220, width: 45, height: 120 };
    h.context.el.aiHandFan.dataset = { seat };
    h.context.el.aiHandFan.lastElementChild = { offsetWidth: 82, offsetHeight: 120, getBoundingClientRect: () => rect };
    let spec;
    h.context.animateCardFlight = async (from, to, card) => { spec = card; };
    await h.context.handleEndTurn();
    assert.equal(spec.sourceRotation, 0);
    assert.equal(spec.sourceWidth, 45); assert.equal(spec.sourceHeight, 120);
  }
});

test('bot purchases and consecutive plays update the displayed count at each landing', async () => {
  const h = setup([{ payload: { command: { ActorId: 'dealer', PowerId: 'clash_draw_card' }, abilityResolution: { applications: [moved('dealer', ['drawn'])] } } }, cardFrame('clash_red_reverse'), cardFrame('clash_red_7')]);
  await h.context.handleEndTurn();
  assert.deepEqual(h.handUpdates, [{ time: 360, count: 8 }, { time: 1380, count: 7 }, { time: 2380, count: 6 }]);
});
test('color selection, CLASH and passing each have one second of visible explanation', async () => {
  const action = powerId => ({ payload: { command: { ActorId: 'dealer', PowerId: powerId } } });
  const h = setup([action('clash_choose_red'), action('clash_shout_clash'), { payload: { command: { ActorId: 'dealer', ActionType: 'END_TURN' } } }]);
  await h.context.handleEndTurn();
  assert.deepEqual(h.bannerTimes, [0, 1000, 2000]); assert.deepEqual(h.pauses, [1000, 1000, 1000]);
  assert.match(h.banners[0], /Vermelho/); assert.match(h.banners[1], /CLASH/); assert.match(h.banners[2], /passou a vez/);
});
test('long animations already satisfy the minimum and get no extra delay', async () => {
  const h = setup([cardFrame('clash_red_7'), cardFrame('clash_blue_4')], { animationMs: 1500 });
  await h.context.handleEndTurn();
  assert.deepEqual(h.pauses, []); assert.deepEqual(h.bannerTimes, [0, 1500]); assert.equal(h.clock, 3000);
});
test('player input stays locked until the final bot presentation delay is complete', async () => {
  const h = setup([cardFrame('clash_red_7')], { deferPauses: true });
  const pending = h.context.handleEndTurn();
  for (let turn = 0; turn < 20 && !h.pendingTimers.length; turn++) await Promise.resolve();
  assert.equal(h.pendingTimers.length, 1); assert.equal(h.state.busy, true);
  h.pendingTimers.shift()(); await pending; assert.equal(h.state.busy, false);
});
