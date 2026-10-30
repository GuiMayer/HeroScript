const assert = require('node:assert/strict');
const path = require('node:path');
const { RealDeckClient } = require('./real-deck-client.cjs');
const base = process.argv[2] || 'http://localhost:15262';
const mode = process.argv[3] || 'bot-accept-six';
const store = process.argv[4] || path.resolve('.runtime/clash-real-deck-fixture/runs');
async function main() {
  const game = await new RealDeckClient(base, store).start(3);
  const play = async id => { const card = (await game.legal()).candidates.find(candidate => candidate.cardDefinitionId === id); assert.ok(card, `Missing playable ${id}`); return game.execute(card); };
  const first = mode === 'bot-starts' ? 'clash_red_7' : 'clash_red_draw2';
  await play(first); await game.end();
  const debt = mode === 'bot-starts' ? 2 : 4;
  assert.equal(game.resource('player', 'clash_draw2_pending'), debt);
  assert.equal(game.zone('hand', 'player').length, 6, 'No immediate draw when countering');
  const legal = (await game.legal()).candidates;
  assert.ok(!legal.some(candidate => candidate.cardDefinitionId === 'clash_wild_draw4'), '+4 cannot join +2');
  assert.ok(!legal.some(candidate => candidate.actionId === 'clash_draw_card'), 'Normal draw cannot bypass debt');
  assert.ok(!legal.some(candidate => candidate.command.actionType === 'END_TURN'), 'Server must block passing while debt is pending');
  await game.command('END_TURN', { actorId: 'player', actionType: 'END_TURN' }, 422);
  if (mode === 'accept-four') {
    const accept = legal.find(candidate => candidate.actionId === 'clash_accept_draw2'); assert.ok(accept);
    const before = game.zone('draw').length; await game.execute(accept);
    assert.equal(game.zone('hand', 'player').length, 10); assert.equal(game.zone('draw').length, before - 4);
    assert.equal(game.resource('player', 'clash_energy'), 0);
    console.log('PASS: +2 +2 -> player buys 4 real cards and loses play'); return;
  }
  const wildFourId = game.zone('hand', 'player').find(id => game.state.Deck.Topology.Instances[id].DefinitionId === 'clash_wild_draw4');
  await game.command('PLAY_CARD', { actorId: 'player', actionType: 'PLAY_CARD', cardInstanceId: wildFourId, targetIds: ['dealer'] }, 422);
  await play('clash_green_draw2');
  assert.equal(game.resource('dealer', 'clash_draw2_pending'), debt + 2);
  const before = game.zone('draw').length;
  await game.end();
  assert.equal(game.zone('hand', 'dealer').length, 6 + debt + 2);
  assert.equal(game.zone('draw').length, before - debt - 2);
  assert.equal(game.resource('dealer', 'clash_draw2_pending'), 0); assert.equal(game.resource('dealer', 'clash_energy'), 0);
  console.log(`PASS: cross-color response -> bot buys ${debt + 2} real cards; +4 is rejected`);
}
main().catch(error => { console.error(error); process.exitCode = 1; });
