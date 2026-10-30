const assert = require('node:assert/strict');
const { RealDeckClient } = require('./real-deck-client.cjs');
async function main() {
  const mode = process.argv[4];
  const game = await new RealDeckClient(process.argv[2], process.argv[3]).start(3, 0);
  const table = game.zone('table')[0];
  const candidates = (await game.legal()).candidates;
  if (mode === 'recycle') {
    assert.equal(game.zone('discard').length, 93);
    const draw = candidates.find(candidate => candidate.actionId === 'clash_draw_card'); assert.ok(draw);
    await game.execute(draw);
    assert.equal(game.zone('hand', 'player').length, 8); assert.equal(game.zone('draw').length, 92);
    assert.equal(game.zone('discard').length, 0); assert.equal(game.zone('table')[0], table);
    console.log('PASS: empty draw pile recycles only discarded cards and keeps the active top card');
  } else {
    assert.equal(game.zone('hand', 'player').length, 100); assert.equal(game.zone('hand', 'dealer').length, 7);
    assert.equal(game.zone('discard').length, 0);
    const accept = candidates.find(candidate => candidate.actionId === 'clash_accept_draw2'); assert.ok(accept);
    const response = await game.execute(accept);
    assert.equal(game.zone('hand', 'player').length, 100); assert.equal(game.resource('player', 'clash_draw2_pending'), 0);
    assert.equal(game.resource('player', 'clash_energy'), 0); assert.equal(game.zone('table')[0], table);
    const moved = response.state.resolution.frames.flatMap(frame => frame.payload?.abilityResolution?.applications || [])
      .flatMap(application => application.CardZoneSteps || []);
    assert.equal(moved.length, 0);
    console.log('PASS: exhausted deck settles penalty without creating cards or phantom draw movements');
  }
  game.assertConservation();
}
main().catch(error => { console.error(error); process.exitCode = 1; });
