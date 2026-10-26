import { deepClone, JsonObject, Signal } from '../contracts/types';

/**
 * Cancellable read handle. Port of scripts/engine/read_ticket.gd
 */
export class ReadTicket {
  readonly completed = new Signal<void>();
  readonly cancelled = new Signal<void>();
  done = false;
  isCancelled = false;
  response: JsonObject = {};
  private resolvers: Array<(res: JsonObject) => void> = [];

  finish(value: JsonObject): void {
    if (this.done) return;
    this.done = true;
    this.response = deepClone(value);
    this.completed.emit();
    for (const resolve of this.resolvers.splice(0)) {
      resolve(deepClone(this.response));
    }
  }

  cancel(): void {
    if (this.done) return;
    this.isCancelled = true;
    this.done = true;
    this.response = { ok: false, cancelled: true, status: 0, data: null };
    this.cancelled.emit();
    this.completed.emit();
    for (const resolve of this.resolvers.splice(0)) {
      resolve(deepClone(this.response));
    }
  }

  async result(): Promise<JsonObject> {
    if (this.done) {
      return deepClone(this.response);
    }
    return new Promise<JsonObject>((resolve) => {
      this.resolvers.push(resolve);
    });
  }
}
