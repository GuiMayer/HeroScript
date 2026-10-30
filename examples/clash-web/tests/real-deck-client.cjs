const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const allowed = ['actorId', 'actionType', 'powerId', 'targetId', 'costOptionId', 'cardInstanceId', 'targetIds'];
const payload = value => Object.fromEntries(Object.entries(value).filter(([key]) => allowed.includes(key)));
class RealDeckClient {
  constructor(base, store) { this.base = base; this.store = store; this.seen = 0; }
  async api(url, body, expected = 200) {
    const response = await fetch(this.base + url, body ? { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(body) } : {});
    const result = await response.json();
    assert.equal(response.status, expected, `${url}: ${JSON.stringify(result).slice(0, 1000)}`);
    return result;
  }
  read() {
    const directory = path.join(this.store, this.runId, 'commits');
    for (const file of fs.readdirSync(directory).filter(name => name.endsWith('.json')).sort()) {
      const sequence = Number(file.split('.')[0]); if (sequence <= this.seen) continue;
      const commit = JSON.parse(fs.readFileSync(path.join(directory, file), 'utf8').replace(/^\uFEFF/, ''));
      if (commit.StateAfter) this.state = commit.StateAfter;
      else for (const patch of commit.StateDelta) {
        const parts = patch.Path.split('/').slice(1).map(token => token.replace(/~1/g, '/').replace(/~0/g, '~'));
        let node = this.state; for (const part of parts.slice(0, -1)) node = node[part];
        const key = parts.at(-1);
        if (Array.isArray(node)) {
          const index = key === '-' ? node.length : Number(key);
          if (patch.Operation === 'add') node.splice(index, 0, patch.Value);
          else if (patch.Operation === 'remove') node.splice(index, 1);
          else node[index] = patch.Value;
        } else if (patch.Operation === 'remove') delete node[key]; else node[key] = patch.Value;
      }
      this.seen = sequence;
    }
    return this.state;
  }
  zone(name, owner = '$run') { return Object.values(this.read().Deck.Topology.Zones).find(zone => zone.Address.ZoneId === name && zone.Address.OwnerId === owner)?.InstanceIds || []; }
  combat() { return this.read().Encounters.find(encounter => encounter.Combat.CombatId === this.combatId).Combat; }
  resource(actor, key) { return this.combat().Actors[actor].Components.resources.State.Resources[key].Current; }
  assertConservation() {
    const state = this.read(), topology = state.Deck.Topology;
    const ids = Object.values(topology.Zones).flatMap(zone => zone.InstanceIds);
    assert.equal(ids.length, 108); assert.equal(new Set(ids).size, 108);
    assert.equal(Object.keys(topology.Instances).length, 108);
    if (this.combatId && this.resource('player', 'clash_setup') === 0) {
      assert.equal(this.zone('table').length, 1);
      for (const actor of ['player', 'dealer']) {
        assert.equal(this.resource(actor, 'hand_count'), this.zone('hand', actor).length, `${actor} actual count`);
        const opponent = actor === 'player' ? 'dealer' : 'player';
        assert.equal(this.resource(actor, 'opponent_hand_count'), this.zone('hand', opponent).length, `${actor} opponent count`);
      }
    }
  }
  async start(seed = 3, expectedDraw = 93) {
    const catalog = await this.api('/api/v1/content/settings');
    const revision = catalog.items.find(setting => setting.settingId === 'clash').currentRevision;
    const run = await this.api('/api/v1/runs', { commandId: crypto.randomUUID(), settingId: 'clash', contentRevision: revision,
      runDefinitionId: 'clash_run', playerEntityId: 'player', modeId: 'clash_classic', seed });
    this.runId = run.runId;
    await this.startEncounter();
    const zones = await this.api(`/api/v1/runs/${this.runId}/card-zones`);
    const hand = zones.zones.find(zone => zone.zoneId === 'hand' && zone.ownerId === 'dealer');
    assert.equal(hand.count, 7); assert.equal(hand.cards.length, 7, 'Telemetry needs read access to actual bot cards');
    assert.ok(hand.cards.every(card => this.zone('hand', 'dealer').includes(card.cardInstanceId)));
    assert.equal(this.zone('draw').length, expectedDraw);
    this.assertConservation(); return this;
  }
  async startEncounter() {
    const available = await this.api(`/api/v1/runs/${this.runId}/available-commands`);
    const start = available.commands.find(command => command.type === 'START_ENCOUNTER'); assert.ok(start);
    const encounter = await this.api(`/api/v1/runs/${this.runId}/commands`, { commandId: crypto.randomUUID(), type: start.type,
      expectedSequence: start.expectedSequence, expectedStep: start.expectedStep, payload: start.validPayload });
    this.combatId = encounter.state.activeEncounterId;
    const setup = (await this.legal()).candidates.find(candidate => candidate.actionId === 'clash_setup_table'); assert.ok(setup);
    await this.execute(setup); this.assertConservation();
  }
  async legal() { return this.api(`/api/v1/combats/${this.combatId}/legal-actions`); }
  async command(type, data, expected = 200) {
    const [run, combat] = await Promise.all([this.api(`/api/v1/runs/${this.runId}`), this.api(`/api/v1/combats/${this.combatId}`)]);
    const owned = new Set(this.zone('hand', 'dealer'));
    const response = await this.api(`/api/v1/combats/${this.combatId}/commands`, { commandId: crypto.randomUUID(), type,
      expectedSequence: run.sequence, expectedStep: combat.step, payload: data }, expected);
    if (expected !== 200) return response;
    for (const frame of response.state?.resolution?.frames || []) {
      const info = frame.payload || {}, cmd = info.resolvedCommand || info.command;
      for (const application of info.abilityResolution?.applications || info.cardResolution?.applications || []) {
        for (const step of application.CardZoneSteps || []) if ((step.TargetAddress || '').endsWith('::dealer::hand'))
          for (const id of step.InstanceIds) owned.add(id);
      }
      if (cmd?.ActorId === 'dealer' && cmd.ActionType === 'PLAY_CARD') {
        assert.ok(owned.has(cmd.CardInstanceId), 'Bot must play an instance it owns or just drew'); owned.delete(cmd.CardInstanceId);
        assert.ok(info.cardResolution?.definitionId, 'Bot animation must receive real card identity');
      }
    }
    this.assertConservation(); return response;
  }
  execute(candidate) { return this.command(candidate.source === 'Card' ? 'PLAY_CARD' : 'EXECUTE_ACTION', payload(candidate.command)); }
  end() { assert.equal(this.resource('player', 'clash_draw2_pending'), 0, 'Accept penalty before passing'); return this.command('END_TURN', { actorId: 'player', actionType: 'END_TURN' }); }
}
module.exports = { RealDeckClient };
