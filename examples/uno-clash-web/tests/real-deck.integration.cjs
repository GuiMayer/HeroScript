const assert = require('node:assert/strict');
const path = require('node:path');
const { RealDeckClient } = require('./real-deck-client.cjs');
async function main() {
  const game = await new RealDeckClient(process.argv[2] || 'http://localhost:15260',
    process.argv[3] || path.join(process.env.LOCALAPPDATA, 'clash-web', 'runs')).start(Number(process.argv[4] || 3));
  console.log('PASS: one physical 108-card deck; 7 real cards each, 93 in draw and 1 on table');
  const stolen = game.zone('hand', 'dealer')[0];
  await game.command('PLAY_CARD', { actorId: 'player', actionType: 'PLAY_CARD', cardInstanceId: stolen, targetIds: ['dealer'] }, 422);
  console.log('PASS: player cannot play a card from the bot hand');
  let ended = false, actions = 0;
  for (; actions < 150; actions++) {
    const status = await game.api(`/api/v1/combats/${game.combatId}`);
    if (!['ACTIVE', 'InProgress'].includes(status.status)) { ended = true; break; }
    const candidates = (await game.legal()).candidates;
    const choose = candidates.find(candidate => candidate.actionId === 'clash_choose_blue') || candidates.find(candidate => candidate.actionId?.startsWith('clash_choose_'));
    const shout = game.resource('player', 'hand_count') === 1 && !game.resource('player', 'clash_called') && candidates.find(candidate => candidate.actionId === 'clash_shout_clash');
    const card = candidates.find(candidate => candidate.source === 'Card');
    const accept = candidates.find(candidate => candidate.actionId === 'clash_accept_draw2');
    const draw = candidates.find(candidate => candidate.actionId === 'clash_draw_card');
    const candidate = choose || shout || card || accept || draw;
    if (candidate) await game.execute(candidate); else await game.end();
    if (actions % 10 === 0) console.log(`Checked ${actions + 1} actions: conservation, real hand counts and bot ownership`);
  }
  assert.ok(ended, 'Match must finish');
  console.log(`PASS: complete match after ${actions} player commands, all moves preserve 108 unique instances`);

}
main().catch(error => { console.error(error); process.exitCode = 1; });
