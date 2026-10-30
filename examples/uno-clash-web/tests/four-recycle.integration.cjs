const assert = require('node:assert/strict');
async function main() {
  const base = process.argv[2] || 'http://localhost:15263';
  const api = async (route, body) => {
    const response = await fetch(base + route, body ? { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(body) } : {});
    const data = await response.json(); assert.ok(response.ok, JSON.stringify(data).slice(0, 500)); return data;
  };
  const setting = (await api('/api/v1/content/settings')).items.find(setting => setting.settingId === 'circular-table');
  const run = await api('/api/v1/runs', { commandId: crypto.randomUUID(), settingId: setting.settingId, contentRevision: setting.currentRevision, ...setting.launch, seed: 3 });
  const start = (await api(`/api/v1/runs/${run.runId}/available-commands`)).commands.find(command => command.type === 'START_ENCOUNTER');
  const encounter = await api(`/api/v1/runs/${run.runId}/commands`, { commandId: crypto.randomUUID(), type: start.type, expectedSequence: start.expectedSequence, expectedStep: start.expectedStep, payload: start.validPayload });
  const combatId = encounter.state.activeEncounterId;
  const zones = async () => (await api(`/api/v1/runs/${run.runId}/card-zones`)).zones;
  const before = await zones();
  const zone = (snapshot, id, owner = '$run') => snapshot.find(zone => zone.zoneId === id && zone.ownerId === owner);
  assert.equal(zone(before, 'draw').count, 0); assert.equal(zone(before, 'discard').count, 79);
  assert.equal(before.filter(zone => zone.zoneId === 'hand').length, 4);
  const activeCard = zone(before, 'table').cards[0].cardInstanceId;
  const candidate = (await api(`/api/v1/combats/${combatId}/legal-actions`)).candidates.find(candidate => candidate.actionId === 't_draw');
  assert.ok(candidate);
  const [runState, combat] = await Promise.all([api(`/api/v1/runs/${run.runId}`), api(`/api/v1/combats/${combatId}`)]);
  const command = Object.fromEntries(Object.entries(candidate.command).filter(([key]) => ['actorId', 'actionType', 'powerId', 'targetIds', 'targetId'].includes(key)));
  const response = await api(`/api/v1/combats/${combatId}/commands`, { commandId: crypto.randomUUID(), type: 'EXECUTE_ACTION', expectedSequence: runState.sequence, expectedStep: combat.step, payload: command });
  const after = await zones();
  assert.equal(zone(after, 'table').cards[0].cardInstanceId, activeCard);
  assert.equal(zone(after, 'draw').count, 78); assert.equal(zone(after, 'discard').count, 0);
  assert.equal(zone(after, 'hand', 'seat_0').count, 8);
  assert.equal(after.reduce((total, zone) => total + zone.count, 0), 108);
  const movements = response.state.resolution.frames.flatMap(frame => frame.payload?.abilityResolution?.applications || []).flatMap(application => application.CardZoneSteps || []);
  assert.ok(movements.some(step => step.SourceAddress?.endsWith('::discard') && step.TargetAddress?.endsWith('::draw')));
  console.log('PASS: four players recycle 79 shared cards, draw one, retain the active card and conserve all 108');
}
main().catch(error => { console.error(error); process.exitCode = 1; });
