import { deepClone, JsonObject } from '../contracts/types';
import { EngineGateway } from './engineGateway';
import { TransportAdapter } from './httpTransport';
import { RequestNumbers } from './requestNumbers';

/**
 * REST adapter for deck compartments. Uncertain commits retain their command identity.
 * Port of scripts/engine/deck_compartment_gateway.gd
 */
export class DeckCompartmentGateway {
  private _transport: TransportAdapter;
  private _commands: EngineGateway;
  runId: string;
  pending: JsonObject = {};

  constructor(transport: TransportAdapter, commands?: EngineGateway, id = '') {
    this._transport = transport;
    this._commands = commands ?? new EngineGateway(transport);
    this.runId = id;
  }

  async readMode(modeId: string, playerId: string, seed: number): Promise<JsonObject> {
    return this._transport.request(
      'GET',
      `/api/v1/deck-compartments/options?modeId=${encodeURIComponent(modeId)}&playerEntityId=${encodeURIComponent(playerId)}&seed=${RequestNumbers.integerToken(seed)}`
    );
  }

  async previewMode(
    modeId: string,
    playerId: string,
    seed: number,
    allocations: any[]
  ): Promise<JsonObject> {
    return this._transport.request('POST', '/api/v1/deck-compartments/preview', {
      modeId,
      playerEntityId: playerId,
      seed: RequestNumbers.integerToken(seed),
      allocations: deepClone(allocations),
    });
  }

  async startRunWithCompartments(
    modeId: string,
    playerId: string,
    seed: number,
    allocations: any[]
  ): Promise<JsonObject> {
    return this._transport.request('POST', '/api/v1/runs', {
      modeId,
      playerEntityId: playerId,
      seed: RequestNumbers.integerToken(seed),
      deckCompartmentAllocations: deepClone(allocations),
    });
  }

  async read(): Promise<JsonObject> {
    return this._transport.request(
      'GET',
      `/api/v1/runs/${encodeURIComponent(this.runId)}/deck-compartments`
    );
  }

  async preview(snapshot: JsonObject, allocations: any[]): Promise<JsonObject> {
    return this._transport.request(
      'POST',
      `/api/v1/runs/${encodeURIComponent(this.runId)}/deck-compartments/preview`,
      {
        expectedSequence: RequestNumbers.integerToken(snapshot.runSequence),
        expectedStep: RequestNumbers.integerToken(snapshot.expectedStep),
        allocations: deepClone(allocations),
      }
    );
  }

  async apply(snapshot: JsonObject, allocations: any[]): Promise<JsonObject> {
    if (Object.keys(this.pending).length === 0) {
      this.pending = this._commands.prepareCommand(
        'runs',
        this.runId,
        'APPLY_DECK_COMPARTMENTS',
        {
          actorId: snapshot.actorId,
          allocations: deepClone(allocations),
        },
        Number(snapshot.runSequence ?? 0),
        Number(snapshot.expectedStep ?? 0)
      );
    }
    const result = await this._commands.sendPrepared(this.pending);
    const status = Number(result.status ?? 0);
    if (result.ok || (status >= 400 && status < 500)) {
      this.pending = {};
    }
    return result;
  }
}
