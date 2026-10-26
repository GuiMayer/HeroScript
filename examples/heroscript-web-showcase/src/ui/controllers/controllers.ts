import { ActivityChoices } from "../../application/activityChoices";
import { GameSession } from "../../application/gameSession";
import { Preferences, Resolution, SettingsSchema } from "../../application/preferences";
import { deepClone, deepEqual, isPlainObject, JsonObject, Signal } from "../../contracts/types";
import { ReadTicket } from "../../engine/readTicket";
import { ActivityPresenter } from "../../presentation/activityPresenter";
import { CombatPresenter } from "../../presentation/combatPresenter";
import { I18n } from "../../presentation/i18n";
import { Playback } from "../../presentation/playback";
import { RunReplayPresenter } from "../../presentation/timelineAndReplayPresenters";
import { HudModel } from "../../presentation/viewModels";

export enum CombatInputState {
  READY = 0,
  SELECTING = 1,
  SUBMITTING = 2,
  ANIMATING = 3,
  RECOVERING = 4,
  PAUSED = 5,
}

export class CombatUiState {
  selectedCard = "";
  selectedTarget = "";
  submitting = false;
  scope = "";

  static phase(
    submittingNow: boolean,
    synchronized: boolean,
    animating: boolean,
    paused: boolean,
    selected: string
  ): CombatInputState {
    if (paused) return CombatInputState.PAUSED;
    if (submittingNow) return CombatInputState.SUBMITTING;
    if (!synchronized) return CombatInputState.RECOVERING;
    if (animating) return CombatInputState.ANIMATING;
    return selected.length === 0
      ? CombatInputState.READY
      : CombatInputState.SELECTING;
  }

  clear(): void {
    this.selectedCard = "";
    this.selectedTarget = "";
  }
}

/**
 * Coordinates engine-published options. No nodes, styling, transport or combat math.
 */
export class CombatController {
  readonly changed = new Signal<void>();
  state = new CombatUiState();
  presenter: CombatPresenter | null = null;
  presentationPending = false;

  private _session: GameSession;
  private _playback: Playback;
  private _paused: () => boolean;
  private _alive = true;
  private _epoch = 0;

  constructor(session: GameSession, playback: Playback, paused: () => boolean) {
    this._session = session;
    this._playback = playback;
    this._paused = paused;
  }

  bind(projection: CombatPresenter, scope: string, cardIds: string[]): void {
    this.presenter = projection;
    if (this.state.scope !== scope) {
      this.state.clear();
      this.state.submitting = false;
      this._epoch += 1;
    }
    this.state.scope = scope;
    if (!cardIds.includes(this.state.selectedCard)) {
      this.state.clear();
    }
  }

  locked(): boolean {
    if (!this._alive) return true;
    const phase = CombatUiState.phase(
      this.state.submitting || this._session.busy,
      this._session.synchronized,
      this._animating(),
      this._paused(),
      this.state.selectedCard
    );
    return (
      phase !== CombatInputState.READY && phase !== CombatInputState.SELECTING
    );
  }

  private _animating(): boolean {
    return this.presentationPending || this._playback.hasBlockingWork();
  }

  interactionModel(): HudModel {
    return new HudModel({
      selected_card: this.state.selectedCard,
      selected_target: this.state.selectedTarget,
      submitting: this.state.submitting,
      busy: this._session.busy,
      synchronized: this._session.synchronized,
      locked: this.locked(),
      has_frames: this._animating(),
      remaining_frames: Math.max(this._playback.total - this._playback.index, 0),
      phase: CombatUiState.phase(
        this.state.submitting || this._session.busy,
        this._session.synchronized,
        this._animating(),
        this._paused(),
        this.state.selectedCard
      ),
    });
  }

  selectCard(id: string): boolean {
    if (this.locked()) return false;
    this.state.selectedCard = id;
    this.state.selectedTarget = "";
    this.changed.emit();
    return true;
  }

  targetOptions(actorId: string): JsonObject[] {
    if (this.locked() || !this.state.selectedCard || !this.presenter) {
      return [];
    }
    const matches = this.presenter
      .candidates(this.state.selectedCard)
      .filter((option) => this.presenter!.targets(option).includes(actorId));
    if (matches.length === 0) return [];
    this.state.selectedTarget = actorId;
    this.changed.emit();
    return matches;
  }

  cancel(): boolean {
    if (this.state.submitting || this._session.busy) return false;
    this.state.clear();
    this.changed.emit();
    return true;
  }

  async submit(candidate: JsonObject): Promise<boolean> {
    if (this.locked() || Object.keys(candidate).length === 0 || !this.presenter) {
      return false;
    }
    const command = isPlainObject(candidate.command) ? candidate.command : {};
    const id = String(command.cardInstanceId ?? "");
    const options = this.presenter.candidates(id);
    if (!options.some((opt) => deepEqual(opt, candidate))) {
      return false;
    }
    const ticket = this._epoch;
    this.state.submitting = true;
    this.changed.emit();
    const accepted = await this._session.submitCandidate(deepClone(candidate));
    if (!this._alive) return false;
    if (ticket !== this._epoch) return accepted;
    this.state.submitting = false;
    if (accepted) {
      this.state.clear();
    }
    this.changed.emit();
    return accepted;
  }

  async executeZoneFlow(payload: JsonObject): Promise<boolean> {
    if (this.locked()) return false;
    const ticket = this._epoch;
    this.state.submitting = true;
    this.changed.emit();
    const accepted = await this._session.executeRunCommand(
      "INVOKE_CARD_ZONE_FLOW",
      deepClone(payload),
      String(payload.flowId ?? "")
    );
    if (!this._alive) return false;
    if (ticket !== this._epoch) return accepted;
    this.state.submitting = false;
    if (accepted) {
      this.state.clear();
    }
    this.changed.emit();
    return accepted;
  }

  async reconnect(): Promise<boolean> {
    return await this._session.refresh();
  }

  async inspectHand(targets: string[]): Promise<JsonObject> {
    return await this._session.inspectHand(targets);
  }

  async inspectCard(id: string): Promise<JsonObject> {
    return await this._session.inspectCard(id);
  }

  dispose(): void {
    this._alive = false;
    this.state.submitting = false;
    this.presentationPending = false;
    this._epoch += 1;
  }

  blockingReasons(): string[] {
    const reasons: string[] = [];
    if (!this._alive) reasons.push("disposed");
    if (this.state.submitting) reasons.push("submitting");
    if (this._session.busy) reasons.push("session_busy");
    if (!this._session.synchronized) reasons.push("unsynchronized");
    if (this.presentationPending) reasons.push("presentation_pending");
    if (this._playback.hasBlockingWork()) reasons.push("animation");
    if (this._paused()) reasons.push("paused");
    return reasons;
  }

  async submitOption(referenceId: string, cardId: string): Promise<boolean> {
    if (!this.presenter) return false;
    for (const option of this.presenter.actionOptions(cardId)) {
      if (option.reference === referenceId) {
        return await this.submit(option.read<JsonObject>("candidate", {}));
      }
    }
    return false;
  }
}

/**
 * Application coordination for published activity choices, no UI construction.
 */
export class ActivityController {
  private _session: GameSession;
  private _alive = true;
  private _epoch = 0;
  private _scope = "";
  private _choices: JsonObject[] = [];
  verifying = false;
  recovering = false;
  automaticRecoveryAttempted = false;
  autoAdvancing = false;

  constructor(session: GameSession) {
    this._session = session;
  }

  bind(run: JsonObject, choices: JsonObject[]): void {
    const scope = `${run.runId ?? ""}/${run.branchId ?? ""}`;
    if (scope !== this._scope) {
      this._epoch += 1;
      this.automaticRecoveryAttempted = false;
    }
    this._scope = scope;
    this._choices = deepClone(choices);
  }

  static optionReference(choice: JsonObject): string {
    return ActivityPresenter.optionReference(choice);
  }

  choiceFor(referenceId: string): JsonObject {
    const matches = this._choices.filter(
      (choice) => ActivityController.optionReference(choice) === referenceId
    );
    return matches.length === 1 ? deepClone(matches[0]) : {};
  }

  async execute(choice: JsonObject): Promise<boolean> {
    if (
      !this._alive ||
      this._session.busy ||
      !this._choices.some((c) => deepEqual(c, choice))
    ) {
      return false;
    }
    const ticket = this._epoch;
    const accepted = await this._session.submitActivity(deepClone(choice));
    return this._alive && ticket === this._epoch && accepted;
  }

  async previewTransformation(choice: JsonObject): Promise<JsonObject> {
    if (!this._alive || !this._choices.some((c) => deepEqual(c, choice))) {
      return {};
    }
    const ticket = this._epoch;
    const result = await this._session.previewTransformation(deepClone(choice));
    return this._alive && ticket === this._epoch ? result : {};
  }

  async recover(): Promise<boolean> {
    if (this.recovering || !this._alive || this._session.busy) return false;
    this.recovering = true;
    const ticket = this._epoch;
    const accepted = await this._session.refresh();
    this.recovering = false;
    return this._alive && ticket === this._epoch && accepted;
  }

  forcedAdvance(): JsonObject {
    return deepClone(ActivityChoices.onlyForcedAdvance(this._choices));
  }

  onlyCompletion(): JsonObject {
    return ActivityChoices.onlyCompletion(this._choices);
  }

  async finishForMap(): Promise<boolean> {
    if (!this._alive || this._session.busy || !this._session.synchronized) {
      return false;
    }
    const run = this._session.run;
    this.bind(run, this._session.activityChoices());
    const resolutions = this._choices.filter((choice) => {
      const payload = isPlainObject(choice.payload) ? choice.payload : {};
      return (
        String(choice.type ?? "") === "RESOLVE_NODE" &&
        String(payload.currentNodeId ?? "") === String(run.currentNodeId ?? "")
      );
    });
    if (resolutions.length === 0) return true;
    if (resolutions.length !== 1) return false;
    return await this.execute(resolutions[0]);
  }

  async verify(): Promise<JsonObject> {
    if (this.verifying || !this._alive) return { ok: false };
    this.verifying = true;
    const ticket = this._epoch;
    const result = await this._session.verify();
    this.verifying = false;
    return this._alive && ticket === this._epoch ? result : { ok: false };
  }

  dispose(): void {
    this._alive = false;
    this._epoch += 1;
  }
}

/**
 * Published option coordination only; never interprets narrative consequences.
 */
export class DialogueController {
  readonly changed = new Signal<void>();
  private _session: GameSession;
  private _alive = true;
  private _epoch = 0;
  private _scope = "";
  private _choices: JsonObject[] = [];
  private _model: JsonObject = {};
  submitting = false;

  constructor(session: GameSession) {
    this._session = session;
  }

  bind(run: JsonObject, choices: JsonObject[], model: JsonObject): void {
    const scope = JSON.stringify([
      run.runId ?? "",
      run.branchId ?? "",
      run.currentNodeId ?? "",
      model.dialogueInstanceId ?? "",
      model.nodeId ?? "",
      model.contentRevision ?? "",
    ]);
    if (scope !== this._scope) {
      this._epoch += 1;
    }
    this._scope = scope;
    this._choices = deepClone(choices);
    this._model = deepClone(model);
  }

  locked(): boolean {
    return (
      !this._alive ||
      this.submitting ||
      this._session.busy ||
      !this._session.synchronized
    );
  }

  async submit(reference: string): Promise<boolean> {
    if (this.locked()) return false;
    const options = Array.isArray(this._model.options)
      ? (this._model.options as JsonObject[])
      : [];
    for (const option of options) {
      if (
        String(option.reference ?? "") === reference &&
        Boolean(option.available) &&
        isPlainObject(option.command)
      ) {
        return await this._send(option.command);
      }
    }
    return false;
  }

  command(type: string): JsonObject {
    const matches = this._choices.filter(
      (choice) => String(choice.type ?? "") === type
    );
    return matches.length === 1 ? deepClone(matches[0]) : {};
  }

  async executePublished(type: string): Promise<boolean> {
    const choice = this.command(type);
    return Object.keys(choice).length > 0 && !this.locked()
      ? await this._send(choice)
      : false;
  }

  private async _send(choice: JsonObject): Promise<boolean> {
    if (!this._choices.some((c) => deepEqual(c, choice))) return false;
    const ticket = this._epoch;
    this.submitting = true;
    this.changed.emit();
    const accepted = await this._session.submitActivity(deepClone(choice));
    if (!this._alive || ticket !== this._epoch) return false;
    this.submitting = false;
    this.changed.emit();
    return accepted;
  }

  async recover(): Promise<boolean> {
    return this._alive && !this.submitting
      ? await this._session.refresh()
      : false;
  }

  dispose(): void {
    this._alive = false;
    this._epoch += 1;
  }
}

/**
 * One coordinator, one cursor. Handles, not a frame countdown, gate the next beat.
 */
export class PresentationDirector {
  readonly changed = new Signal<void>();
  private _playback: Playback;
  private _preferences: Preferences;
  private _paused: () => boolean;
  private _alive = true;
  private _pumping = false;
  pumpBudgetMs = 4.0;
  dispatches = 0;
  instantDispatches = 0;
  budgetYields = 0;
  longestPumpMs = 0;
  watchdogSeconds = 12.0;

  constructor(
    playback: Playback,
    preferences: Preferences,
    paused: () => boolean
  ) {
    this._playback = playback;
    this._preferences = preferences;
    this._paused = paused;
  }

  advance(): boolean {
    return this._pump(true);
  }

  private _pump(manual = false): boolean {
    if (
      !this._alive ||
      this._pumping ||
      this._paused() ||
      this._playback.activeHandle !== null
    ) {
      return false;
    }
    this._pumping = true;
    const ticket = this._playback.generation;
    const start =
      typeof performance !== "undefined" ? performance.now() : Date.now();
    let advanced = false;

    while (
      this._alive &&
      ticket === this._playback.generation &&
      !this._paused() &&
      this._playback.activeHandle === null &&
      this._playback.hasFrames()
    ) {
      const passive = this._playback.nextIsPassive();
      if (!this._preferences.autoAnimations && !passive && !manual) break;
      if (!this._playback.advance()) break;
      this.dispatches += 1;
      advanced = true;
      if (!passive) manual = false;
      const handle = this._playback.activeHandle as any;
      if (handle === null) {
        this.instantDispatches += 1;
      } else {
        handle.completed.connect(() => {
          if (
            this._alive &&
            !this._pumping &&
            ticket === this._playback.generation
          ) {
            this._pump();
            this.changed.emit();
          }
        }, true);
      }
      const now =
        typeof performance !== "undefined" ? performance.now() : Date.now();
      if (now - start >= this.pumpBudgetMs) {
        if (handle === null && this._playback.hasFrames()) {
          this.budgetYields += 1;
        }
        break;
      }
    }

    const end =
      typeof performance !== "undefined" ? performance.now() : Date.now();
    this.longestPumpMs = Math.max(this.longestPumpMs, end - start);
    this._pumping = false;
    if (advanced && this._alive) {
      this.changed.emit();
    }
    return advanced;
  }

  metrics(): JsonObject {
    return {
      dispatches: this.dispatches,
      instant: this.instantDispatches,
      cpu_yields: this.budgetYields,
      longest_pump_us: Math.round(this.longestPumpMs * 1000),
    };
  }

  resetMetrics(): void {
    this.dispatches = 0;
    this.instantDispatches = 0;
    this.budgetYields = 0;
    this.longestPumpMs = 0;
  }

  process(deltaSeconds: number): void {
    if (!this._alive || this._paused()) return;
    const handle = this._playback.activeHandle;
    if (handle !== null) {
      handle.elapsed += deltaSeconds;
      if (handle.lostOwnedMotion()) {
        handle.finish("fallback");
      } else if (!handle.claimed) {
        handle.finish();
      } else if (handle.elapsed > this.watchdogSeconds) {
        handle.finish("fallback");
      }
      if (this._playback.activeHandle !== null) return;
      this.changed.emit();
    }
    this._pump();
  }

  skip(): void {
    if (!this._alive || this._paused()) return;
    this._playback.skip();
    this.changed.emit();
  }

  dispose(): void {
    this._alive = false;
    this._playback.clear();
  }
}

/**
 * Disposable read cursor; never submits game commands.
 */
export class RunReplayController {
  static readonly PAGE_SIZE = 25;
  readonly changed = new Signal<void>();

  session!: GameSession;
  translator!: I18n;
  runId = "";
  endpointContext: unknown[] = [];
  entries: JsonObject[] = [];
  selectedIndex = -1;
  throughSequence = 0;
  nextCursor = 0;
  hasMore = true;
  pageLoading = false;
  pageError = false;
  presentationLoading = false;
  disposed = false;
  playing = false;
  verifying = false;
  stateText = "";
  frameText = "";
  commitCache: Record<string, JsonObject> = {};
  maxCacheEntries = 32;
  maxCacheBytes = 8 * 1024 * 1024;
  cacheBytes = 0;
  inspectionAllowed = false;

  private _canInspect = true;
  private _cacheSizes: Record<string, number> = {};
  private _cacheOrder: string[] = [];
  private _pending: Record<string, ReadTicket> = {};
  private _scope: unknown[] = [];
  private _pageTicket: ReadTicket | null = null;
  private _presentationTicket: ReadTicket | null = null;
  private _epoch = 0;
  playEpoch = 0;
  startedMs = 0;
  firstContentMs = -1;

  setup(client: GameSession, id: string, i18n: I18n): void {
    this.session = client;
    this.runId = id;
    this.translator = i18n;
    this.endpointContext = this.session.replayContext();
  }

  valid(): boolean {
    return (
      !this.disposed &&
      deepEqual(this.endpointContext, this.session.replayContext())
    );
  }

  model(): JsonObject {
    return {
      entries: this.entries,
      selected_index: this.selectedIndex,
      through_sequence: this.throughSequence,
      has_more: this.hasMore,
      page_loading: this.pageLoading,
      page_error: this.pageError,
      presentation_loading: this.presentationLoading,
      playing: this.playing,
      verifying: this.verifying,
      state_text: this.stateText,
      frame_text: this.frameText,
      inspection_allowed: this.inspectionAllowed,
    };
  }

  async start(): Promise<void> {
    this.startedMs = Date.now();
    await this.loadMore();
    if (this.valid() && this.entries.length > 0) {
      await this.select(0);
    }
  }

  async loadMore(): Promise<void> {
    if (!this.valid() || !this.hasMore) return;
    if (this.pageLoading) {
      if (this._pageTicket !== null) {
        await this._pageTicket.result();
      }
      return;
    }
    this.pageLoading = true;
    this.pageError = false;
    this.changed.emit();

    this._pageTicket = this.session.beginReplayPage(
      this.runId,
      this.nextCursor,
      RunReplayController.PAGE_SIZE,
      this.throughSequence
    );
    const response = await this._pageTicket.result();
    if (!this.valid()) return;
    this.pageLoading = false;
    if (!response.ok) {
      this.pageError = true;
      if (!response.cancelled) {
        this.stateText = this.translator.error(response);
      }
      this.changed.emit();
      return;
    }

    const page = isPlainObject(response.data) ? response.data : {};
    if ("runId" in page && String(page.runId) !== this.runId) {
      this.pageError = true;
      this.stateText = this.translator.text("Replay snapshot changed.");
      this.changed.emit();
      return;
    }

    const scope = [
      page.settingId ?? "",
      page.lineage ?? {},
      page.contentRevision ?? "",
    ];
    if (this._scope.length > 0 && !deepEqual(this._scope, scope)) {
      this.pageError = true;
      this.stateText = this.translator.text("Replay snapshot changed.");
      this.changed.emit();
      return;
    }

    const incoming = Array.isArray(page.items)
      ? (page.items as JsonObject[])
      : [];
    const incomingHead = Math.trunc(Number(page.throughSequence ?? 0));
    if (
      this.throughSequence !== 0 &&
      this.throughSequence !== incomingHead
    ) {
      this.pageError = true;
      this.stateText = this.translator.text("Replay snapshot changed.");
      this.changed.emit();
      return;
    }

    let expected = this.nextCursor + 1;
    for (const item of incoming) {
      if (Math.trunc(Number(item.sequence ?? 0)) !== expected) {
        this.pageError = true;
        this.stateText = this.translator.text("Replay page is not contiguous.");
        this.changed.emit();
        return;
      }
      expected += 1;
    }

    const cursor = Math.trunc(Number(page.nextCursor ?? this.nextCursor));
    if (
      cursor !== expected - 1 ||
      incomingHead < cursor ||
      (Boolean(page.hasMore) && incoming.length === 0)
    ) {
      this.pageError = true;
      this.stateText = this.translator.text("Replay page is not contiguous.");
      this.changed.emit();
      return;
    }

    this._scope = scope;
    this.throughSequence = incomingHead;
    this._canInspect = Boolean(page.historicalInspectionAllowed ?? true);
    this.entries.push(...incoming);
    this.nextCursor = cursor;
    this.hasMore = Boolean(page.hasMore);
    if (this.entries.length === 0) {
      this.stateText = this.translator.text(
        "This journey has no recorded commands."
      );
    }
    this.changed.emit();
  }

  async select(index: number): Promise<void> {
    if (!this.valid() || index < 0 || index >= this.entries.length) return;
    this._epoch += 1;
    const epoch = this._epoch;
    this.selectedIndex = index;
    const entry = this.entries[index];
    this.frameText = RunReplayPresenter.framesText(entry, this.translator);
    this.stateText = this.translator.text("Loading saved state…");
    this.presentationLoading = true;
    this.changed.emit();

    if (!this._canInspect) {
      this.presentationLoading = false;
      this.inspectionAllowed = false;
      this.stateText = `${this.translator.text(
        "Historical inspection is unavailable in this mode."
      )}\n${this.translator.text(
        "State hash: %s",
        String(entry.stateHash ?? "")
      )}`;
      if (this.firstContentMs < 0) {
        this.firstContentMs = Date.now() - this.startedMs;
      }
      this.changed.emit();
      return;
    }

    const sequence = Math.trunc(Number(entry.sequence ?? 0));
    const key = JSON.stringify([
      this.endpointContext,
      this.runId,
      this._scope,
      sequence,
      entry.stateHash ?? "",
    ]);

    for (const old of Object.keys(this._pending)) {
      if (old !== key) {
        this._pending[old].cancel();
        delete this._pending[old];
      }
    }

    let response: JsonObject =
      key in this.commitCache ? { ok: true, data: this.commitCache[key] } : {};
    if (Object.keys(response).length > 0) {
      this._touch(key);
    } else {
      if (!(key in this._pending)) {
        this._pending[key] = this.session.beginReplayPresentation(
          this.runId,
          sequence
        );
      }
      const request = this._pending[key];
      this._presentationTicket = request;
      response = await request.result();
      if (this._pending[key] === request) {
        delete this._pending[key];
      }
    }

    if (!this.valid() || epoch !== this._epoch) return;
    this.presentationLoading = false;
    if (!response.ok) {
      this.inspectionAllowed = false;
      this.stateText =
        response.status === 403
          ? this.translator.text(
              "Historical inspection is unavailable in this mode."
            )
          : this.translator.error(response);
      if (response.status === 403 && this.firstContentMs < 0) {
        this.firstContentMs = Date.now() - this.startedMs;
      }
      this.changed.emit();
      return;
    }

    const projection = isPlainObject(response.data) ? response.data : {};
    if (
      String(projection.runId ?? "") !== this.runId ||
      Math.trunc(Number(projection.sequence ?? 0)) !== sequence ||
      projection.stateHash !== entry.stateHash
    ) {
      this.stateText = this.translator.text(
        "Replay presentation does not match its command."
      );
      this.changed.emit();
      return;
    }

    this.inspectionAllowed = true;
    this._cache(key, projection);
    this.stateText = RunReplayPresenter.summaryText(
      projection,
      this.translator
    );
    const transcript = RunReplayPresenter.transcriptText(
      projection,
      this.translator
    );
    if (transcript.length > 0) {
      this.frameText = transcript;
    }
    if (this.firstContentMs < 0) {
      this.firstContentMs = Date.now() - this.startedMs;
    }
    this.changed.emit();
  }

  private _touch(key: string): void {
    const idx = this._cacheOrder.indexOf(key);
    if (idx >= 0) this._cacheOrder.splice(idx, 1);
    this._cacheOrder.push(key);
  }

  private _cache(key: string, projection: JsonObject): void {
    const bytes = JSON.stringify(projection).length;
    if (bytes > this.maxCacheBytes || this.maxCacheEntries < 1) return;
    this.cacheBytes -= this._cacheSizes[key] ?? 0;
    this.commitCache[key] = deepClone(projection);
    this._cacheSizes[key] = bytes;
    this.cacheBytes += bytes;
    this._touch(key);

    while (
      Object.keys(this.commitCache).length > this.maxCacheEntries ||
      this.cacheBytes > this.maxCacheBytes
    ) {
      const oldest = this._cacheOrder.shift();
      if (!oldest) break;
      this.cacheBytes -= this._cacheSizes[oldest] ?? 0;
      delete this._cacheSizes[oldest];
      delete this.commitCache[oldest];
    }
  }

  async navigate(direction: number): Promise<boolean> {
    if (!this.valid()) return false;
    const index = Math.max(0, this.selectedIndex + direction);
    if (index >= this.entries.length && this.hasMore) {
      await this.loadMore();
    }
    if (
      !this.valid() ||
      index >= this.entries.length ||
      index === this.selectedIndex
    ) {
      return false;
    }
    await this.select(index);
    return this.valid() && !this.pageError;
  }

  stop(): void {
    this.playing = false;
    this.playEpoch += 1;
    this.changed.emit();
  }

  dispose(): void {
    if (this.disposed) return;
    this.disposed = true;
    this.playing = false;
    this._epoch += 1;
    if (this._pageTicket && !this._pageTicket.done) {
      this._pageTicket.cancel();
    }
    if (this._presentationTicket && !this._presentationTicket.done) {
      this._presentationTicket.cancel();
    }
    for (const ticket of Object.values(this._pending)) {
      ticket.cancel();
    }
    this._pending = {};
    this.commitCache = {};
    this._cacheSizes = {};
    this._cacheOrder = [];
    this.cacheBytes = 0;
    this.entries = [];
  }
}

/**
 * Owns preference mutations/persistence. Views expose only local intents.
 */
export class SettingsController {
  private _preferences: Preferences;
  private _translator: I18n;
  private _session: GameSession;

  constructor(preferences: Preferences, translator: I18n, session: GameSession) {
    this._preferences = preferences;
    this._translator = translator;
    this._session = session;
  }

  read(key: string): unknown {
    return this._preferences.get(key);
  }

  setOption(key: string, value: unknown, persist = false): boolean {
    const spec = SettingsSchema.option(key);
    if (Object.keys(spec).length === 0) return false;
    if (spec.values && !spec.values.includes(String(value))) return false;
    if (spec.type === "bool" && typeof value !== "boolean") return false;
    let finalValue = value;
    if (spec.min !== undefined) {
      if (typeof value !== "number" || !Number.isFinite(value)) return false;
      finalValue = Math.min(Math.max(value, spec.min), spec.max ?? 1);
    }
    this._preferences.set(key, finalValue);
    if (persist) {
      this.commit();
    }
    return true;
  }

  commit(): void {
    this._preferences.save();
    this._translator.setLocale(String(this._preferences.get("locale")));
  }

  actions(): string[] {
    return [...Preferences.ACTIONS];
  }

  actionLabel(action: string): string {
    return this._preferences.actionLabel(action);
  }

  controllerLabel(action: string): string {
    return this._preferences.controllerLabel(action);
  }

  remapKey(action: string, key: string, modifiers = false): string {
    return this._preferences.remapKey(action, key, modifiers);
  }

  remapButton(action: string, buttonIndex: number): string {
    return this._preferences.remapButton(action, buttonIndex);
  }

  resetBindings(): void {
    this._preferences.resetBindings();
  }

  resolutionOptions(): Resolution[] {
    return this._preferences.resolutionOptions();
  }

  setResolution(value: Resolution): void {
    this._preferences.setResolution(value);
  }

  saveConnection(value: string): string {
    const normalized = Preferences.normalizeApiUrl(value);
    if (!normalized) return "Use an HTTP or HTTPS address.";
    if (normalized === this._preferences.apiUrl) return "";
    if (this._session.busy || this._session.hasPendingCommand) {
      return "Finish or recover the current operation before changing the connection.";
    }
    if (!this._preferences.setApiUrl(normalized)) {
      return "Use an HTTP or HTTPS address.";
    }
    return "";
  }
}
