import { Signal } from '../contracts/types';

export interface TweenLike {
  isValid(): boolean;
  isRunning(): boolean;
  kill(): void;
  onFinished(cb: () => void): void;
}

/**
 * Exactly-once completion for blocking motion; decorative tails use separate owners.
 * Port of scripts/presentation/presentation_handle.gd
 */
export class PresentationHandle {
  readonly started = new Signal<void>();
  readonly impact = new Signal<void>();
  readonly completed = new Signal<string>();

  done = false;
  result = '';
  claimed = false;
  elapsed = 0.0;

  private _started = false;
  private _hit = false;
  private _tweens: TweenLike[] = [];
  private _cleanup: Array<() => void> = [];

  begin(): void {
    if (this.done || this._started) return;
    this._started = true;
    this.started.emit();
  }

  hit(): void {
    if (this.done || this._hit) return;
    this._hit = true;
    this.impact.emit();
  }

  own(tween: TweenLike): void {
    this.claimed = true;
    this._tweens.push(tween);
  }

  cleanup(action: () => void): void {
    this._cleanup.push(action);
  }

  lostOwnedMotion(): boolean {
    return (
      !this.done &&
      this._tweens.length > 0 &&
      this._tweens.every((t) => t === null || !t.isValid())
    );
  }

  join(operations: TweenLike[]): void {
    const running = operations.filter((t) => t && t.isValid() && t.isRunning());
    if (running.length === 0) {
      this.hit();
      this.finish();
      return;
    }
    let remaining = running.length;
    for (const tween of running) {
      this.own(tween);
      tween.onFinished(() => {
        if (this.done) return;
        remaining -= 1;
        if (remaining === 0) {
          this.finish();
        }
      });
    }
  }

  finish(outcome = 'completed'): void {
    if (this.done) return;
    this.done = true;
    this.result = outcome;
    for (const tween of this._tweens) {
      if (tween && tween.isValid()) {
        tween.kill();
      }
    }
    this._tweens = [];
    for (const action of this._cleanup) {
      action();
    }
    this._cleanup = [];
    this.completed.emit(outcome);
  }
}
