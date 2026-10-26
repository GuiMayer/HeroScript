import { deepClone, isPlainObject, JsonObject, Signal } from '../contracts/types';
import { PresentationEnvelope } from './presentationEnvelope';
import { PresentationHandle } from './presentationHandle';
import { PresentationPlanner } from './presentationPlanner';

/**
 * Disposable presentation cursor. Never sends commands or owns game state.
 * Port of scripts/presentation/playback.gd
 */
export class Playback {
  readonly framePresented = new Signal<JsonObject & { frame: JsonObject; index: number; total: number }>();
  readonly settled = new Signal<{ result: string; generation: number }>();
  readonly changed = new Signal<void>();

  activeHandle: PresentationHandle | null = null;
  executor: ((frame: JsonObject, handle: PresentationHandle) => void) | null = null;
  visualResolver: ((beat: JsonObject) => boolean) | null = null;
  dispatchReady = true;
  envelope: PresentationEnvelope | null = null;
  index = 0;
  generation = 0;

  private _frames: JsonObject[] = [];
  private _measurements: JsonObject[] = [];

  get total(): number {
    return this._frames.length;
  }

  get frames(): JsonObject[] {
    return deepClone(this._frames);
  }

  measurements(): JsonObject[] {
    return deepClone(this._measurements);
  }

  releaseFacts(): void {
    if (this.hasBlockingWork()) return;
    this._frames = [];
    this.envelope = null;
  }

  loadEnvelope(value: PresentationEnvelope): void {
    this.loadReceipt(value.receipt());
    this.envelope = value;
    this.dispatchReady = false;
    this.changed.emit();
  }

  enqueueEnvelope(value: PresentationEnvelope | JsonObject | JsonObject[]): void {
    if (value instanceof PresentationEnvelope) {
      this.loadEnvelope(value);
      this.activate();
      this.changed.emit();
      return;
    }
    if (Array.isArray(value)) {
      this.clear();
      this._frames = deepClone(value);
      this.index = 0;
      this.dispatchReady = true;
      this.changed.emit();
      return;
    }
    if (isPlainObject(value) && Array.isArray((value as any).frames)) {
      this.clear();
      this._frames = deepClone((value as any).frames);
      this.index = 0;
      this.dispatchReady = true;
      this.changed.emit();
      return;
    }
    if (isPlainObject(value)) {
      this.loadReceipt(value);
      this.dispatchReady = true;
      this.changed.emit();
    }
  }

  activate(prefix: JsonObject[] = []): void {
    this._frames = [...deepClone(prefix), ...this._frames];
    this.dispatchReady = true;
  }

  annotate(values: JsonObject): void {
    for (const frame of this._frames) {
      Object.assign(frame, deepClone(values));
    }
  }

  loadReceipt(receipt: JsonObject): void {
    this.clear();
    const state = PresentationEnvelope.dictionary(receipt.state);
    const resolution = state.resolution;
    const framesData = isPlainObject(resolution) ? resolution.frames : null;
    this._frames = Array.isArray(framesData) ? Playback.presentationFrames(framesData) : [];
    this.index = 0;
  }

  static presentationFrames(engineFrames: any[]): JsonObject[] {
    return PresentationPlanner.plan(engineFrames);
  }

  clear(_runId = ''): void {
    this.generation += 1;
    const old = this.activeHandle;
    this.activeHandle = null;
    if (old !== null) {
      old.finish('cancelled');
    }
    this._frames = [];
    this.index = 0;
    this.envelope = null;
    this.dispatchReady = true;
  }

  hasFrames(): boolean {
    return this.index < this._frames.length;
  }

  nextIsPassive(): boolean {
    if (!this.hasFrames()) return false;
    const beat = this._frames[this.index];
    if (!this.executor) return true;
    if (this.visualResolver) return !this.visualResolver(beat);
    const transition = String(beat.transitionType ?? '');
    return (
      transition.length > 0 &&
      transition !== 'effect' &&
      !beat.hasVisibleFacts &&
      beat.presentationOnly !== 'card_departure'
    );
  }

  hasBlockingWork(): boolean {
    return this.activeHandle !== null || this.hasFrames();
  }

  skip(): void {
    this.index = this._frames.length;
    if (this.activeHandle !== null) {
      this.activeHandle.finish('skipped');
    } else {
      this.settled.emit({ result: 'skipped', generation: this.generation });
    }
  }

  completeActive(): void {
    if (this.activeHandle !== null) {
      this.activeHandle.finish();
    }
  }

  advance(): boolean {
    if (!this.dispatchReady || this.activeHandle !== null || !this.hasFrames()) {
      return false;
    }
    const current = this.index;
    this.index += 1;
    const ticket = this.generation;
    const handle = new PresentationHandle();
    this.activeHandle = handle;

    const unsubCompleted = handle.completed.connect((outcome) => {
      unsubCompleted();
      if (this.generation !== ticket || this.activeHandle !== handle) return;
      this.activeHandle = null;
      if (!this.hasFrames()) {
        if (this.envelope !== null) {
          this.envelope.mark('blocking_completed');
          this._measurements.push({
            scope: this.envelope.scope(),
            result: outcome,
            timings: this.envelope.timings(),
          });
          if (this._measurements.length > 64) {
            this._measurements.shift();
          }
        }
        this.settled.emit({ result: outcome, generation: ticket });
      }
    });

    if (this.envelope !== null) {
      this.envelope.mark('first_cue');
      const unsubImpact = handle.impact.connect(() => {
        unsubImpact();
        if (this.envelope !== null && this.generation === ticket) {
          this.envelope.mark('first_impact');
        }
      });
    }

    handle.begin();
    const frame = deepClone(this._frames[current]);
    this.framePresented.emit({
      ...frame,
      frame,
      index: current,
      total: this._frames.length,
    });
    this.changed.emit();
    if (this.executor) {
      handle.claimed = true;
      this.executor(frame, handle);
    } else {
      handle.finish();
    }
    return true;
  }
}
