import { SnapshotProjection } from '../contracts/snapshotProjection';
import { deepClone, deepEqual, isPlainObject, JsonObject } from '../contracts/types';
import { TransportAdapter } from './httpTransport';
import { ReadTicket } from './readTicket';
import { RequestNumbers } from './requestNumbers';

/**
 * REST contract adapter. No UI, localization, preferences or animation dependencies.
 * Port of scripts/engine/engine_gateway.gd
 */
export class EngineGateway {
  private _transport: TransportAdapter;
  private _campaignRequest: JsonObject = {};

  constructor(transport: TransportAdapter) {
    this._transport = transport;
  }

  async connectEngine(): Promise<JsonObject> {
    const health = await this._transport.request('GET', '/api/v1/health/ready');
    if (!health.ok) {
      return health;
    }
    return this._transport.request('GET', '/api/v1/content/settings');
  }

  async createCampaign(seed: number, setting: JsonObject): Promise<JsonObject> {
    const launch = isPlainObject(setting.launch) ? setting.launch : {};
    const settingId = String(setting.settingId ?? '');
    const body: JsonObject = {
      settingId,
      runDefinitionId: String(launch.runDefinitionId ?? ''),
      playerEntityId: String(launch.playerEntityId ?? 'player'),
      modeId: String(launch.modeId ?? ''),
      contentRevision: String(setting.currentRevision ?? ''),
      seed: RequestNumbers.integerToken(seed),
    };
    const previous = deepClone(this._campaignRequest);
    delete previous.commandId;
    if (!deepEqual(previous, body)) {
      this._campaignRequest = deepClone(body);
      this._campaignRequest.commandId = this.uuid();
    }
    const response = await this._transport.request(
      'POST',
      '/api/v1/runs',
      deepClone(this._campaignRequest)
    );
    const status = Number(response.status ?? 0);
    if (response.ok || (status >= 400 && status < 500)) {
      this._campaignRequest = {};
    }
    return response;
  }

  async createSandbox(
    modeId: string,
    scenario: JsonObject,
    seed: number,
    revision: string,
    settingId: string
  ): Promise<JsonObject> {
    let body: JsonObject = {
      ...deepClone(scenario),
      schemaVersion: 2,
      modeId,
      contentRevision: revision,
      seed: RequestNumbers.integerToken(seed),
      attemptKey: `web-${modeId}-${this.uuid()}`,
    };
    body = RequestNumbers.fields(body, RequestNumbers.SCENARIO_PATHS);
    return this._transport.request(
      'POST',
      `/api/v1/sandbox/runs?configName=${encodeURIComponent(settingId)}`,
      body
    );
  }

  async readRun(runId: string): Promise<JsonObject> {
    return this._transport.request('GET', `/api/v1/runs/${encodeURIComponent(runId)}`);
  }

  async readLoadout(runId: string, actorId: string): Promise<JsonObject> {
    return this._transport.request(
      'GET',
      `/api/v1/runs/${encodeURIComponent(runId)}/loadout?actorId=${encodeURIComponent(actorId)}`
    );
  }

  async readLoadoutOptions(modeId: string, playerId: string, seed: number): Promise<JsonObject> {
    return this._transport.request(
      'GET',
      `/api/v1/inscriptions/loadout-options?modeId=${encodeURIComponent(modeId)}&playerEntityId=${encodeURIComponent(playerId)}&seed=${RequestNumbers.integerToken(seed)}`
    );
  }

  async previewLoadoutOptions(
    modeId: string,
    playerId: string,
    seed: number,
    assignments: Array<{ slotId: string; inscriptionId: string }>
  ): Promise<JsonObject> {
    return this._transport.request('POST', '/api/v1/inscriptions/loadout-preview', {
      modeId,
      playerEntityId: playerId,
      seed: RequestNumbers.integerToken(seed),
      assignments: deepClone(assignments),
    });
  }

  async readDeckCompartments(runId: string): Promise<JsonObject> {
    return this._transport.request(
      'GET',
      `/api/v1/runs/${encodeURIComponent(runId)}/deck-compartments`
    );
  }

  async previewDeckCompartments(snapshot: JsonObject, allocations: any[]): Promise<JsonObject> {
    return this._transport.request(
      'POST',
      `/api/v1/runs/${encodeURIComponent(String(snapshot.runId))}/deck-compartments/preview`,
      {
        expectedSequence: RequestNumbers.integerToken(snapshot.runSequence),
        expectedStep: RequestNumbers.integerToken(snapshot.expectedStep),
        allocations: deepClone(allocations),
      }
    );
  }

  async previewLoadout(snapshot: JsonObject, assignments: JsonObject): Promise<JsonObject> {
    return this._transport.request(
      'POST',
      `/api/v1/runs/${encodeURIComponent(String(snapshot.runId))}/loadout/preview`,
      {
        actorId: snapshot.actorId,
        assignments: deepClone(assignments),
        expectedSequence: RequestNumbers.integerToken(snapshot.runSequence),
        expectedStep: RequestNumbers.integerToken(snapshot.expectedStep),
      }
    );
  }

  async readSession(runId: string, expectedSequence = -1): Promise<JsonObject> {
    const query = expectedSequence >= 0 ? `?expectedSequence=${expectedSequence}` : '';
    return this._transport.request('GET', `/api/v1/runs/${encodeURIComponent(runId)}/session${query}`);
  }

  async readCardPresentations(runId: string): Promise<JsonObject> {
    return this._transport.request(
      'GET',
      `/api/v1/runs/${encodeURIComponent(runId)}/cards/presentations`
    );
  }

  async runHistory(playerId: string, settingId: string): Promise<JsonObject> {
    const response = await this._transport.request(
      'GET',
      `/api/v1/profiles/${encodeURIComponent(playerId)}?settingId=${encodeURIComponent(settingId)}`
    );
    if (
      response.ok &&
      (String(response.data?.settingId ?? '') !== settingId ||
        String(response.data?.playerId ?? '') !== playerId)
    ) {
      return {
        ok: false,
        errorKey: 'Restart the engine to enable setting-specific profiles.',
      };
    }
    if (response.ok && isPlainObject(response.data)) {
      const data = response.data;
      response.data = {
        playerId: data.playerId ?? playerId,
        settingId: data.settingId ?? settingId,
        revision: data.revision ?? '',
        totalRuns: data.totalRuns ?? 0,
        completedRuns: data.completedRuns ?? 0,
        activeRuns: data.activeRuns ?? 0,
        unlocks: Array.isArray(data.unlocks) ? deepClone(data.unlocks) : [],
        progressSequence: data.progressSequence ?? 0,
        progressRevision: data.progressRevision ?? '',
        unlockProofs: Array.isArray(data.unlockProofs) ? deepClone(data.unlockProofs) : [],
        availability: isPlainObject(data.availability) ? deepClone(data.availability) : {},
        achievements: Array.isArray(data.achievements) ? deepClone(data.achievements) : [],
        items: Array.isArray(data.runs) ? deepClone(data.runs) : [],
      };
    }
    return response;
  }

  async runTimeline(
    runId: string,
    afterSequence = 0,
    limit = 25,
    throughSequence = 0
  ): Promise<JsonObject> {
    return this._transport.request(
      'GET',
      this.timelinePath(runId, afterSequence, limit, throughSequence)
    );
  }

  private timelinePath(
    runId: string,
    afterSequence: number,
    limit: number,
    throughSequence: number
  ): string {
    const through = throughSequence > 0 ? `&throughSequence=${throughSequence}` : '';
    return `/api/v1/runs/${encodeURIComponent(runId)}/timeline?afterSequence=${afterSequence}&limit=${limit}${through}`;
  }

  replayContext(): [string, number] {
    return this._transport.endpointContext();
  }

  beginRunTimeline(
    runId: string,
    afterSequence: number,
    limit: number,
    throughSequence: number
  ): ReadTicket {
    return this._transport.read(this.timelinePath(runId, afterSequence, limit, throughSequence));
  }

  beginRunPresentation(runId: string, sequence: number): ReadTicket {
    return this._transport.read(
      `/api/v1/runs/${encodeURIComponent(runId)}/commits/${sequence}/presentation`
    );
  }

  beginRunCommit(runId: string, sequence: number): ReadTicket {
    return this._transport.read(`/api/v1/runs/${encodeURIComponent(runId)}/commits/${sequence}`);
  }

  async runCommit(runId: string, sequence: number): Promise<JsonObject> {
    return this._transport.request(
      'GET',
      `/api/v1/runs/${encodeURIComponent(runId)}/commits/${sequence}`
    );
  }

  async readLegalActions(combatId: string, actorId: string): Promise<JsonObject> {
    return this._transport.request(
      'GET',
      `/api/v1/combats/${encodeURIComponent(combatId)}/legal-actions?actorId=${encodeURIComponent(actorId)}`
    );
  }

  async modifierItemPreview(runId: string, itemId: string, cardId: string): Promise<JsonObject> {
    return this._transport.request(
      'GET',
      `/api/v1/runs/${encodeURIComponent(runId)}/card-modifier-items/${encodeURIComponent(itemId)}/preview/${encodeURIComponent(cardId)}`
    );
  }

  prepareCommand(
    scope: string,
    id: string,
    type: string,
    payload: JsonObject,
    sequence: number,
    step: number
  ): JsonObject {
    if (scope !== 'runs' && scope !== 'combats') {
      throw new Error(`Invalid command scope: ${scope}`);
    }
    return {
      scope,
      id,
      envelope: {
        commandId: this.uuid(),
        expectedSequence: RequestNumbers.integerToken(sequence),
        expectedStep: RequestNumbers.integerToken(step),
        type,
        payload: RequestNumbers.payload(type, payload),
      },
    };
  }

  async sendPrepared(command: JsonObject): Promise<JsonObject> {
    return this._transport.request(
      'POST',
      `/api/v1/${command.scope}/${encodeURIComponent(String(command.id))}/commands`,
      deepClone(command.envelope)
    );
  }

  async timeline(combatId: string, afterSequence = 0, limit = 50): Promise<JsonObject> {
    return this._transport.request(
      'GET',
      `/api/v1/combats/${encodeURIComponent(combatId)}/timeline?afterSequence=${afterSequence}&limit=${limit}`
    );
  }

  async branchTree(runId: string): Promise<JsonObject> {
    return this._transport.request('GET', `/api/v1/runs/${encodeURIComponent(runId)}/branch-tree`);
  }

  async createBranch(combatId: string, sequence: number, key: string): Promise<JsonObject> {
    return this._transport.request(
      'POST',
      `/api/v1/combats/${encodeURIComponent(combatId)}/timeline/${sequence}/branches`,
      { branchKey: key }
    );
  }

  async verify(runId: string): Promise<JsonObject> {
    return this._transport.request(
      'POST',
      `/api/v1/runs/${encodeURIComponent(runId)}/verify`,
      null,
      120.0
    );
  }

  async simulate(runId: string, sequence: number, commands: any[]): Promise<JsonObject> {
    const normalized = deepClone(commands);
    for (const command of normalized) {
      if (isPlainObject(command) && isPlainObject(command.payload)) {
        command.payload = RequestNumbers.payload(String(command.type ?? ''), command.payload);
      }
    }
    return this._transport.request('POST', '/api/v1/simulations', {
      sourceRunId: runId,
      sourceSequence: RequestNumbers.integerToken(sequence),
      commands: normalized,
    });
  }

  async content(
    kind: string,
    revision: string,
    settingId: string,
    limit: number
  ): Promise<JsonObject> {
    return this._transport.request(
      'GET',
      `/api/v1/content/${encodeURIComponent(kind)}?revision=${encodeURIComponent(revision)}&configName=${encodeURIComponent(settingId)}&limit=${limit}`
    );
  }

  static candidatePayload(command: JsonObject): JsonObject {
    const payload: JsonObject = {};
    for (const key of [
      'actorId',
      'actionType',
      'powerId',
      'targetId',
      'targetIds',
      'costOptionId',
      'cardInstanceId',
    ]) {
      if (key in command && command[key] !== null && command[key] !== undefined) {
        payload[key] = command[key];
      }
    }
    return deepClone(payload);
  }

  candidatePayload(command: JsonObject): JsonObject {
    return EngineGateway.candidatePayload(command);
  }

  async inspectHand(
    combatId: string,
    actorId: string,
    targetIds: string[] = []
  ): Promise<JsonObject> {
    let path = `/api/v1/combats/${encodeURIComponent(combatId)}/cards/evaluations?actorId=${encodeURIComponent(actorId)}`;
    for (const targetId of targetIds) {
      path += `&targetIds=${encodeURIComponent(String(targetId))}`;
    }
    return this._transport.request('GET', path);
  }

  async inspectCard(combatId: string, actorId: string, instanceId: string): Promise<JsonObject> {
    return this._transport.request(
      'GET',
      `/api/v1/combats/${encodeURIComponent(combatId)}/cards/${encodeURIComponent(instanceId)}/evaluation?actorId=${encodeURIComponent(actorId)}`
    );
  }

  async transformationPreview(
    runId: string,
    cardId: string,
    operation: string,
    upgradeId = '',
    transformationId: any = null
  ): Promise<JsonObject> {
    let path = `/api/v1/runs/${encodeURIComponent(runId)}/cards/${encodeURIComponent(cardId)}/transformation-preview?operation=${encodeURIComponent(operation)}`;
    if (upgradeId.length > 0) {
      path += `&upgradeId=${encodeURIComponent(upgradeId)}`;
    }
    if (transformationId !== null && transformationId !== undefined) {
      path += `&transformationId=${encodeURIComponent(String(RequestNumbers.integerToken(transformationId)))}`;
    }
    return this._transport.request('GET', path);
  }

  async historicalState(combatId: string, sequence: number): Promise<JsonObject> {
    const response = await this._transport.request(
      'GET',
      `/api/v1/combats/${encodeURIComponent(combatId)}/timeline/${sequence}/state`
    );
    if (response.ok && isPlainObject(response.data)) {
      response.data = EngineGateway.normalizeHistory(response.data);
    }
    return response;
  }

  async resolution(combatId: string, commandId: string): Promise<JsonObject> {
    return this._transport.request(
      'GET',
      `/api/v1/combats/${encodeURIComponent(combatId)}/resolutions/${encodeURIComponent(commandId)}`
    );
  }

  static normalizeHistory(data: JsonObject): JsonObject {
    const result = deepClone(data);
    result.combat = SnapshotProjection.combat(result.combat ?? {});
    result.run = SnapshotProjection.run(result.run ?? {});
    return result;
  }

  private uuid(): string {
    if (typeof crypto !== 'undefined' && typeof crypto.randomUUID === 'function') {
      return crypto.randomUUID();
    }
    return 'xxxxxxxx-xxxx-4xxx-yxxx-xxxxxxxxxxxx'.replace(/[xy]/g, (c) => {
      const r = (Math.random() * 16) | 0;
      const v = c === 'x' ? r : (r & 0x3) | 0x8;
      return v.toString(16);
    });
  }
}
