const assert = require('node:assert/strict');
const { RealDeckClient } = require('./real-deck-client.cjs');
async function main() {
  const game = await new RealDeckClient(process.argv[2], process.argv[3]).start(3);
  const readHand = async () => (await game.api(`/api/v1/runs/${game.runId}/card-zones`)).zones.find(zone => zone.zoneId === 'hand' && zone.ownerId === 'dealer');
  const before = await readHand(); assert.equal(before.cards.length, 7);
  for (const card of before.cards) {
    assert.ok(game.zone('hand', 'dealer').includes(card.cardInstanceId));
    assert.equal(card.definitionId, game.state.Deck.Topology.Instances[card.cardInstanceId].DefinitionId);
  }
  await game.command('PLAY_CARD', { actorId: 'player', actionType: 'PLAY_CARD', cardInstanceId: before.cards[0].cardInstanceId, targetIds: ['dealer'] }, 422);
  const response = await game.end();
  const play = response.state.resolution.frames.find(frame => frame.payload?.cardResolution?.definitionId);
  assert.ok(play);
  const after = await readHand(); assert.equal(after.cards.length, 6);
  assert.ok(!after.cards.some(card => card.cardInstanceId === play.payload.consumedCardId));
  assert.deepEqual(new Set(after.cards.map(card => card.cardInstanceId)), new Set(game.zone('hand', 'dealer')));
  console.log('PASS: telemetry reads actual bot cards, updates after a play, and grants no permission to play them');
}
main().catch(error => { console.error(error); process.exitCode = 1; });
