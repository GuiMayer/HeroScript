const assert = require('node:assert/strict');
const path = require('node:path');
const { RealDeckClient } = require('./real-deck-client.cjs');
const modes = require('../game-modes.js');

async function main() {
  const base = process.argv[2] || 'http://localhost:15260';
  const store = process.argv[3] || path.join(process.env.LOCALAPPDATA, 'clash-web', 'runs');
  const api = async (route, body, expected = 200) => {
    const response = await fetch(base + route, body ? { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(body) } : {});
    const data = await response.json();
    assert.equal(response.status, expected, route + ': ' + JSON.stringify(data).slice(0, 800));
    return data;
  };
  const catalog = await api('/api/v1/content/settings');
  for (const [key, mode] of Object.entries(modes)) {
    const setting = catalog.items.find(setting => setting.settingId === mode.settingId);
    assert.ok(setting?.currentRevision, key + ' published');
    const run = await api('/api/v1/runs', { commandId: crypto.randomUUID(), settingId: setting.settingId, contentRevision: setting.currentRevision, ...setting.launch, seed: 3 });
    const available = await api(`/api/v1/runs/${run.runId}/available-commands`);
    const start = available.commands.find(command => command.type === 'START_ENCOUNTER');
    const encounter = await api(`/api/v1/runs/${run.runId}/commands`, { commandId: crypto.randomUUID(), type: start.type, expectedSequence: start.expectedSequence, expectedStep: start.expectedStep, payload: start.validPayload });
    const combatId = encounter.state.activeEncounterId;
    const reader = new RealDeckClient(base, store); reader.runId = run.runId; reader.combatId = combatId;
    const legal = () => api(`/api/v1/combats/${combatId}/legal-actions`);
    const checkPool = () => {
      const topology = reader.read().Deck.Topology;
      const ids = Object.values(topology.Zones).flatMap(zone => zone.InstanceIds);
      assert.equal(ids.length, 108); assert.equal(new Set(ids).size, 108);
      assert.equal(Object.keys(topology.Instances).length, 108);
      assert.equal(Object.values(topology.Zones).find(zone => zone.Address.ZoneId === 'table').InstanceIds.length, 1);
    };
    const command = async candidate => {
      const [runState, combat] = await Promise.all([api(`/api/v1/runs/${run.runId}`), api(`/api/v1/combats/${combatId}`)]);
      const owned = new Map(Object.values(reader.read().Deck.Topology.Zones).filter(zone => zone.Address.ZoneId === 'hand').map(zone => [zone.Address.OwnerId, new Set(zone.InstanceIds)]));
      const type = candidate.source === 'Card' ? 'PLAY_CARD' : candidate.command.actionType === 'END_TURN' ? 'END_TURN' : 'EXECUTE_ACTION';
      const payload = Object.fromEntries(Object.entries(candidate.command).filter(([key]) => ['actorId', 'actionType', 'powerId', 'targetId', 'targetIds', 'cardInstanceId', 'costOptionId'].includes(key)));
      const result = await api(`/api/v1/combats/${combatId}/commands`, { commandId: crypto.randomUUID(), type, expectedSequence: runState.sequence, expectedStep: combat.step, payload });
      for (const frame of result.state?.resolution?.frames || []) {
        const info = frame.payload || {}, resolved = info.resolvedCommand || info.command;
        for (const application of info.cardResolution?.applications || info.abilityResolution?.applications || []) {
          for (const step of application.CardZoneSteps || application.cardZoneSteps || []) {
            const match = (step.TargetAddress || step.targetAddress || '').match(/::([^:]+)::hand$/);
            if (match && owned.has(match[1])) for (const id of step.InstanceIds || step.instanceIds || []) owned.get(match[1]).add(id);
          }
        }
        if (resolved?.ActionType === 'PLAY_CARD') {
          assert.ok(owned.get(resolved.ActorId)?.has(resolved.CardInstanceId), 'Every participant plays an owned physical instance');
          owned.get(resolved.ActorId).delete(resolved.CardInstanceId);
        }
      }
      return result;
    };
    const setup = (await legal()).candidates.find(candidate => candidate.actionId === mode.actions.setup);
    if (setup) await command(setup);
    checkPool();
    const initial = await api(`/api/v1/runs/${run.runId}/card-zones`);
    const hands = initial.zones.filter(zone => zone.zoneId === 'hand');
    assert.equal(hands.length, Object.keys(mode.seats).length + 1);
    assert.ok(hands.every(hand => hand.count === 7));
    assert.equal(initial.zones.find(zone => zone.zoneId === 'draw').count, key === 'duel' ? 93 : 79);
    console.log('PASS ' + key + ': server setup, physical shared pool and real hand ownership');
    if (key === 'duel') continue; // The duel already has its dedicated complete-match suite.
    let finished = false, checkedBlockedPass = false;
    for (let step = 0; step < 250; step++) {
      const combat = await api(`/api/v1/combats/${combatId}`);
      if (combat.status !== 'ACTIVE') {
        assert.ok(combat.participantVictory.actorIds.length > 0);
        finished = true; console.log('PASS four-player complete match: ' + step + ' player commands; winner ' + combat.participantVictory.actorIds.join(', ')); break;
      }
      const choices = await legal();
      const pass = choices.candidates.find(candidate => candidate.command.actionType === 'END_TURN');
      if (!pass && !checkedBlockedPass) {
        const runState = await api(`/api/v1/runs/${run.runId}`);
        await api(`/api/v1/combats/${combatId}/commands`, { commandId: crypto.randomUUID(), type: 'END_TURN', expectedSequence: runState.sequence, expectedStep: combat.step, payload: { actorId: mode.playerId, actionType: 'END_TURN' } }, 422);
        checkedBlockedPass = true;
      }
      const candidate = choices.candidates.find(candidate => candidate.actionId?.startsWith(mode.actions.choose))
        || choices.candidates.find(candidate => candidate.actionId === mode.actions.call)
        || choices.candidates.find(candidate => candidate.source === 'Card')
        || choices.candidates.find(candidate => candidate.actionId === mode.actions.accept)
        || choices.candidates.find(candidate => candidate.actionId === mode.actions.draw) || pass;
      assert.ok(candidate, 'Server must expose an action for the player');
      await command(candidate); checkPool();
    }
    assert.ok(finished, 'Four-player match must end');
  }
}
main().catch(error => { console.error(error); process.exitCode = 1; });
