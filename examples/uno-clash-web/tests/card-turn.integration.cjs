const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const { RealDeckClient } = require('./real-deck-client.cjs');
const source = fs.readFileSync(path.join(__dirname, '..', 'app.js'), 'utf8');
const start = source.indexOf('  function requiresDrawBeforePassing(');
const rules = vm.createContext({}); if (start !== -1) vm.runInContext(source.slice(start, source.indexOf('  function cancelAutoTurn()', start)), rules);
async function main() {
  const mode = process.argv[4];
  const game = await new RealDeckClient(process.argv[2], process.argv[3]).start(3, mode === 'bot-buy-clash' ? 99 : 93);
  if (mode === 'player-extra') {
    let legal = (await game.legal()).candidates;
    assert.equal(legal.filter(candidate => candidate.source === 'Card').length, 0);
    await game.execute(legal.find(candidate => candidate.actionId === 'clash_draw_card'));
    assert.equal(game.resource('player', 'clash_draw_available'), 0);
    legal = (await game.legal()).candidates;
    await game.execute(legal.find(candidate => candidate.cardDefinitionId === 'clash_red_reverse'));
    assert.equal(game.resource('player', 'clash_energy'), 1);
    assert.equal(game.resource('player', 'clash_draw_available'), 1, 'Each card must start a new draw allowance');
    legal = (await game.legal()).candidates;
    assert.equal(legal.filter(candidate => candidate.source === 'Card').length, 0);
    if (rules.requiresDrawBeforePassing) assert.equal(rules.requiresDrawBeforePassing({ legalCardsCount: 0, clashEnergy: 1, drawAvailable: 1,
      pendingDraw2: 0, availableCards: game.zone('draw').length + game.zone('discard').length }), true);
    await game.execute(legal.find(candidate => candidate.actionId === 'clash_draw_card'));
    assert.equal(game.resource('player', 'clash_draw_available'), 0);
    assert.equal(game.zone('hand', 'player').length, 8);
    assert.equal(game.zone('draw').length, 91);
    console.log('PASS: buying then playing Reverse starts a new player turn and requires another purchase without a legal card');
    return;
  }
  // The player may pass with playable cards; the bot has no compatible card.
  const before = (await game.legal()).candidates;
  assert.ok(before.some(candidate => candidate.source === 'Card'));
  const response = await game.end();
  const events = response.state.resolution.frames.map(frame => frame.payload).filter(payload => (payload?.resolvedCommand || payload?.command)?.ActorId === 'dealer');
  const commands = events.map(payload => payload.cardResolution?.definitionId || (payload.resolvedCommand || payload.command).PowerId || (payload.resolvedCommand || payload.command).ActionType);
  if (mode === 'bot-buy-clash') {
    assert.deepEqual(commands, ['clash_catch_missed_clash', 'clash_draw_card', 'END_TURN']);
    assert.equal(game.zone('hand', 'player').length, 3);
    assert.equal(game.zone('hand', 'dealer').length, 8);
    assert.equal(game.zone('draw').length, 96);
    console.log('PASS: catching missed CLASH does not replace the bot mandatory purchase');
  } else if (mode === 'bot-buy') {
    assert.deepEqual(commands, ['clash_draw_card', 'END_TURN']);
    assert.equal(game.zone('hand', 'dealer').length, 8); assert.equal(game.zone('draw').length, 92);
    console.log('PASS: bot without a playable card buys one real card before passing');
  } else {
    assert.deepEqual(commands, ['clash_draw_card', 'clash_red_reverse', 'clash_draw_card', 'END_TURN']);
    assert.equal(game.zone('hand', 'dealer').length, 8); assert.equal(game.zone('draw').length, 91);
    assert.equal(game.resource('dealer', 'clash_draw_available'), 0);
    console.log('PASS: bot buys, plays Reverse, then buys again in its next turn before passing');
  }
  assert.equal(game.combat().ActivationState.ActiveActorId, 'player');
  assert.equal(game.zone('hand', 'player').length, mode === 'bot-buy-clash' ? 3 : 7);
  game.assertConservation();
}
main().catch(error => { console.error(error); process.exitCode = 1; });
