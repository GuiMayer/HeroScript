import { deepClone, isPlainObject, JsonObject, Signal } from '../contracts/types';
import { ReadTicket } from './readTicket';

export type HttpMethod = 'GET' | 'POST' | 0 | 2;

export interface TransportAdapter {
  request(
    method: HttpMethod,
    path: string,
    body?: any,
    timeoutSeconds?: number,
    readTicket?: ReadTicket | null
  ): Promise<JsonObject>;
  read(path: string): ReadTicket;
  endpointContext(): [string, number];
  baseUrl?: string;
  readonly availabilityChanged?: Signal<boolean>;
}

class GetFlight {
  readonly completed = new Signal<void>();
  done = false;
  consumers = 0;
  response: JsonObject = {};
  cancellation = new ReadTicket();
}

interface QueueJob {
  priority: number;
  serial: number;
  epoch: number;
  ticket: ReadTicket | null;
  resolve: (acquired: boolean) => void;
}

/**
 * HTTP Transport with bounded concurrency, priority queue, GET coalescing,
 * endpoint epoch isolation, and cancellation tickets.
 * Port of scripts/engine/http_transport.gd
 */
export class HttpTransport implements TransportAdapter {
  readonly availabilityChanged = new Signal<boolean>();
  available = false;
  lastError = '';
  timings: JsonObject[] = [];
  defaultTimeoutSeconds = 30.0;
  maxWorkers = 4;

  private _baseUrl = 'http://127.0.0.1:5271';
  private _endpointEpoch = 0;
  private _pendingReads: ReadTicket[] = [];
  private _activeWorkers = 0;
  private _queue: QueueJob[] = [];
  private _closing = false;
  private _serial = 0;
  private _getFlights = new Map<string, GetFlight>();
  private _customFetch?: any;
  private _baseUrlResolver?: () => string;

  constructor(
    baseUrlOrFetch?: string | (() => string) | any,
    customFetch?: any
  ) {
    if (typeof baseUrlOrFetch === 'string') {
      this._baseUrl = baseUrlOrFetch;
      this._customFetch = customFetch;
    } else if (typeof baseUrlOrFetch === 'function' && customFetch !== undefined) {
      this._baseUrlResolver = baseUrlOrFetch as () => string;
      this._baseUrl = this._baseUrlResolver();
      this._customFetch = customFetch;
    } else if (typeof baseUrlOrFetch === 'function') {
      // Check if zero-arg URL getter vs fetch function
      try {
        if (baseUrlOrFetch.length === 0) {
          const maybeUrl = baseUrlOrFetch();
          if (typeof maybeUrl === 'string') {
            this._baseUrlResolver = baseUrlOrFetch as () => string;
            this._baseUrl = maybeUrl;
            return;
          }
        }
      } catch {
        // Ignore and treat as customFetch
      }
      this._customFetch = baseUrlOrFetch;
    }
  }

  get baseUrl(): string {
    if (this._baseUrlResolver) {
      const next = this._baseUrlResolver();
      if (typeof next === 'string' && next.length > 0 && next !== this._baseUrl) {
        this._baseUrl = next;
      }
    }
    return this._baseUrl;
  }

  set baseUrl(value: string) {
    if (this._baseUrl === value) return;
    this._baseUrl = value;
    this._endpointEpoch += 1;
    for (const ticket of [...this._pendingReads]) {
      ticket.cancel();
    }
    this.setAvailable(false);
    this.lastError = '';
    this.pumpQueue();
  }

  endpointContext(): [string, number] {
    return [this.baseUrl, this._endpointEpoch];
  }

  begin(method: HttpMethod, path: string, body: any = null): ReadTicket {
    if (this.normalizeMethod(method) === 'GET' && body === null) {
      return this.read(path);
    }
    const ticket = new ReadTicket();
    queueMicrotask(async () => {
      if (ticket.isCancelled) return;
      const res = await this.request(method, path, body, -1, ticket);
      ticket.finish(res);
    });
    return ticket;
  }

  read(path: string): ReadTicket {
    const ticket = new ReadTicket();
    this._pendingReads.push(ticket);
    const cleanup = ticket.completed.connect(() => {
      cleanup();
      const idx = this._pendingReads.indexOf(ticket);
      if (idx >= 0) this._pendingReads.splice(idx, 1);
    });
    queueMicrotask(() => {
      void this.runRead(path, ticket);
    });
    return ticket;
  }

  private async runRead(path: string, ticket: ReadTicket): Promise<void> {
    if (ticket.isCancelled) return;
    const res = await this.request('GET', path, null, -1, ticket);
    ticket.finish(res);
  }

  dispose(): void {
    this._closing = true;
    for (const ticket of [...this._pendingReads]) {
      ticket.cancel();
    }
    this.pumpQueue();
  }

  async requestMany(paths: Record<string, string>): Promise<Record<string, JsonObject>> {
    const entries = Object.entries(paths);
    const results: Record<string, JsonObject> = {};
    await Promise.all(
      entries.map(async ([key, path]) => {
        results[key] = await this.request('GET', path);
      })
    );
    return results;
  }

  private normalizeMethod(method: HttpMethod): 'GET' | 'POST' {
    if (method === 0 || method === 'GET') return 'GET';
    return 'POST';
  }

  async request(
    method: HttpMethod,
    path: string,
    body: any = null,
    timeoutSeconds = -1.0,
    readTicket: ReadTicket | null = null
  ): Promise<JsonObject> {
    const normMethod = this.normalizeMethod(method);
    if (normMethod !== 'GET' || body !== null) {
      return this.performRequest(normMethod, path, body, timeoutSeconds, readTicket);
    }
    if (readTicket && readTicket.isCancelled) {
      return deepClone(readTicket.response);
    }
    const key = JSON.stringify([this.endpointContext(), path, timeoutSeconds]);
    let flight = this._getFlights.get(key);
    if (!flight) {
      flight = new GetFlight();
      this._getFlights.set(key, flight);
      const launchedFlight = flight;
      queueMicrotask(() => {
        void this.launchGet(key, launchedFlight, path, timeoutSeconds);
      });
    }
    flight.consumers += 1;
    const waiter = new ReadTicket();
    const deliver = () => waiter.finish(flight!.response);
    const unsubCompleted = flight.completed.connect(deliver);
    const unsubCancel = readTicket ? readTicket.cancelled.connect(() => waiter.cancel()) : null;
    if (flight.done) {
      deliver();
    }
    const result = await waiter.result();
    unsubCompleted();
    if (unsubCancel) unsubCancel();
    flight.consumers -= 1;
    if (flight.consumers === 0 && !flight.done) {
      if (this._getFlights.get(key) === flight) {
        this._getFlights.delete(key);
      }
      const flightToCancel = flight;
      queueMicrotask(() => flightToCancel.cancellation.cancel());
    }
    return result;
  }

  private async launchGet(
    key: string,
    flight: GetFlight,
    path: string,
    timeoutSeconds: number
  ): Promise<void> {
    flight.response = await this.performRequest(
      'GET',
      path,
      null,
      timeoutSeconds,
      flight.cancellation
    );
    flight.done = true;
    if (this._getFlights.get(key) === flight) {
      this._getFlights.delete(key);
    }
    flight.completed.emit();
  }

  private acquire(
    method: 'GET' | 'POST',
    path: string,
    epoch: number,
    ticket: ReadTicket | null
  ): Promise<boolean> {
    const priority =
      method !== 'GET' ? 0 : path.includes('/timeline') || path.includes('/commits/') ? 2 : 1;
    this._serial += 1;
    return new Promise<boolean>((resolve) => {
      const job: QueueJob = { priority, serial: this._serial, epoch, ticket, resolve };
      this._queue.push(job);
      this.pumpQueue();
    });
  }

  private release(): void {
    this._activeWorkers = Math.max(0, this._activeWorkers - 1);
    this.pumpQueue();
  }

  private pumpQueue(): void {
    // Clean invalid/cancelled jobs first
    for (let i = this._queue.length - 1; i >= 0; i--) {
      const job = this._queue[i];
      if (
        this._closing ||
        job.epoch !== this._endpointEpoch ||
        (job.ticket !== null && job.ticket.isCancelled)
      ) {
        this._queue.splice(i, 1);
        job.resolve(false);
      }
    }
    const limit = Math.max(1, this.maxWorkers);
    while (this._activeWorkers < limit && this._queue.length > 0) {
      let bestIndex = 0;
      for (let i = 1; i < this._queue.length; i++) {
        const candidate = this._queue[i];
        const best = this._queue[bestIndex];
        if (
          candidate.priority < best.priority ||
          (candidate.priority === best.priority && candidate.serial < best.serial)
        ) {
          bestIndex = i;
        }
      }
      const [nextJob] = this._queue.splice(bestIndex, 1);
      this._activeWorkers += 1;
      nextJob.resolve(true);
    }
  }

  private async performRequest(
    method: 'GET' | 'POST',
    path: string,
    body: any = null,
    timeoutSeconds = -1.0,
    readTicket: ReadTicket | null = null
  ): Promise<JsonObject> {
    const started = performance.now();
    const endpointTicket = this._endpointEpoch;
    const acquired = await this.acquire(method, path, endpointTicket, readTicket);
    const queueMs = Math.round(performance.now() - started);
    if (!acquired) {
      if (endpointTicket !== this._endpointEpoch) {
        return {
          ok: false,
          status: 0,
          error: 'The engine connection changed during the request.',
          errorKey: 'The engine connection changed during the request.',
          errorArgs: [],
          data: null,
        };
      }
      return { ok: false, cancelled: true, status: 0, data: null };
    }

    const resolvedTimeout =
      timeoutSeconds <= 0.0 ? this.defaultTimeoutSeconds : Math.max(timeoutSeconds, 1.0);
    const controller = new AbortController();
    const timer = setTimeout(() => controller.abort('timeout'), resolvedTimeout * 1000);
    const unsubCancel = readTicket
      ? readTicket.cancelled.connect(() => {
          controller.abort('cancelled');
          this.pumpQueue();
        })
      : null;

    const headers: Record<string, string> = {
      Accept: 'application/json',
    };
    if (method === 'POST' && path.endsWith('/commands')) {
      headers['Prefer'] = 'return=minimal';
    }
    let encoded: string | undefined;
    if (body !== null && body !== undefined) {
      headers['Content-Type'] = 'application/json';
      encoded = JSON.stringify(body);
    }

    const fetchFn = this._customFetch ?? globalThis.fetch.bind(globalThis);
    const base = this.baseUrl.replace(/\/+$/, '');
    // In browser dev mode on localhost:5173 talking to default 127.0.0.1:5271, direct fetch or Vite proxy works
    const fullUrl = `${base}${path}`;

    let status = 0;
    let raw = '';
    let networkFailed = false;
    let errorReason: any = 'network';

    try {
      const response: any = await fetchFn(fullUrl, {
        method,
        headers,
        body: encoded,
        signal: controller.signal,
      });
      status = Number(response?.status ?? 0);
      if (typeof response?.text === 'function') {
        raw = await response.text();
      } else if (typeof response?.bodyText === 'string') {
        raw = response.bodyText;
      } else if (response?.body !== undefined) {
        raw = typeof response.body === 'string' ? response.body : JSON.stringify(response.body);
      }
    } catch (err: any) {
      networkFailed = true;
      errorReason = err?.message ?? String(err);
    } finally {
      clearTimeout(timer);
      if (unsubCancel) unsubCancel();
      this.release();
    }

    if (readTicket && readTicket.isCancelled) {
      return { ok: false, cancelled: true, status: 0, data: null };
    }
    if (endpointTicket !== this._endpointEpoch) {
      return {
        ok: false,
        status: 0,
        error: 'The engine connection changed during the request.',
        errorKey: 'The engine connection changed during the request.',
        errorArgs: [],
        data: null,
      };
    }
    if (networkFailed) {
      return this.failure('Connection to the engine failed (%s).', 0, null, [errorReason]);
    }

    const transferMs = Math.round(performance.now() - started);
    let parsed: any = {};
    let parseFailed = false;
    if (raw.length > 0) {
      try {
        parsed = JSON.parse(raw);
      } catch {
        parseFailed = true;
        parsed = null;
      }
    }
    const totalMs = Math.round(performance.now() - started);
    const operation = isPlainObject(body) ? String(body.type ?? '') : '';
    this.timings.push({
      queue_ms: queueMs,
      method,
      path,
      operation,
      transfer_ms: transferMs,
      parse_ms: Math.max(0, totalMs - transferMs),
      bytes: raw.length,
      ms: totalMs,
    });
    if (this.timings.length > 200) {
      this.timings.shift();
    }

    if (
      status >= 200 &&
      status < 300 &&
      (parseFailed || (!isPlainObject(parsed) && !Array.isArray(parsed)))
    ) {
      return this.failure('The engine returned an invalid JSON response.');
    }
    if (status < 200 || status >= 300) {
      let message = `HTTP ${status}`;
      if (isPlainObject(parsed) && 'error' in parsed) {
        message = String(parsed.error);
      } else if (isPlainObject(parsed)) {
        message = String(parsed.detail ?? parsed.title ?? message);
      }
      return this.failure(message, status, parsed);
    }

    this.setAvailable(true);
    this.lastError = '';
    return { ok: true, status, data: parsed };
  }

  private failure(
    message: string,
    status = 0,
    data: any = null,
    args: any[] = []
  ): JsonObject {
    let formatted = message;
    if (args.length > 0) {
      let i = 0;
      formatted = message.replace(/%s/g, () => String(args[i++] ?? ''));
    }
    this.lastError = formatted;
    if (status === 0) {
      this.setAvailable(false);
    }
    return {
      ok: false,
      status,
      error: this.lastError,
      errorKey: status === 0 ? message : '',
      errorArgs: args,
      data,
    };
  }

  private setAvailable(value: boolean): void {
    if (this.available === value) return;
    this.available = value;
    this.availabilityChanged.emit(value);
  }
}
