import { deepClone, deepEqual, isPlainObject, JsonObject, Signal } from '../contracts/types';
import { ReadTicket } from '../engine/readTicket';
import { PresentationEnvelope } from '../presentation/presentationEnvelope';
import { ActivityChoices } from './activityChoices';
import { RunResumeIndex } from './runResumeIndex';

/**
 * Authoritative session coordinator.
 * Port of scripts/application/game_session.gd
 */
export class GameSession {
  readonly operationChanged = new Signal<string>();
  readonly changed = new Signal<void>();
  readonly stateChanged = this.changed;
  readonly commandStarted = new Signal<string>();
  readonly receiptReceived = new Signal<JsonObject>();
  readonly presentationReady = new Signal<PresentationEnvelope>();
  readonly presentationDiscarded = new Signal<void>();
  readonly runOpened = new Signal<string>();
  readonly availabilityChanged = new Signal<boolean>();
  readonly commandFinished = new Signal<void>();
  readonly failed = new Signal<JsonObject>();
  readonly errorReported = new Signal<string>();
  readonly noticeReported = new Signal<string>();

  private _preferences: any = null;

  constructor(
    gateway?: any,
    preferences?: any,
    playbackSink?: { enqueueEnvelope?: (envelope: PresentationEnvelope) => void; clear?: () => void }
  ) {
    if (gateway) {
      this.configure(gateway);
    }
    this._preferences = preferences ?? null;
    this.failed.connect((err) => {
      const msg = String(err?.error ?? err?.errorKey ?? 'Engine operation failed.');
      if (msg) {
        this.errorReported.emit(msg);
      }
    });
    if (playbackSink?.enqueueEnvelope) {
      this.presentationReady.connect((env) => playbackSink.enqueueEnvelope!(env));
    }
  }

  private _run: JsonObject = {};
  private _combat: JsonObject = {};
  private _availableCommands: JsonObject[] = [];
  private _legalActions: JsonObject[] = [];
  private _toolCapabilities: Record<string, boolean> = {};
  private _cardZones: JsonObject = {};
  private _characterAttributes: JsonObject = {};
  private _cardPresentations: JsonObject = {};
  private _collectionPresentations: JsonObject = {};
  private _modifierItems: JsonObject = {};
  private _cardEvaluations: JsonObject[] = [];
  private _cardEvaluationContext: any[] = [];
  private _settings: JsonObject[] = [];
  private _acceptedPresentation: JsonObject = {};
  private _commandStartedMs = 0;

  toolProfile = 'normal';
  selectedSettingId = 'default';
  available = false;
  contentRevision = '';
  lastViewCaptureUsec = 0;
  synchronized = true;

  private _gateway: any = null;
  private _operation = 'idle';
  private _generation = 0;
  private _pending: JsonObject = {};

  get run(): JsonObject {
    return deepClone(this._run);
  }

  get combat(): JsonObject {
    return deepClone(this._combat);
  }

  get availableCommands(): JsonObject[] {
    return deepClone(this._availableCommands);
  }

  get legalActions(): JsonObject[] {
    return deepClone(this._legalActions);
  }

  get toolCapabilities(): string[] {
    return Object.keys(this._toolCapabilities);
  }

  get cardZones(): JsonObject {
    return deepClone(this._cardZones);
  }

  get characterAttributes(): JsonObject {
    if (
      String(this._characterAttributes.runId ?? '') === String(this._run.runId ?? '') &&
      Number(this._characterAttributes.runSequence ?? -1) === Number(this._run.sequence ?? 0)
    ) {
      return deepClone(this._characterAttributes);
    }
    return {};
  }

  get cardEvaluationsReady(): boolean {
    return deepEqual(
      this._cardEvaluationContext,
      this.evaluationContext(this._run, this._combat)
    );
  }

  get cardPresentations(): JsonObject[] {
    if (this.belongsToCurrentRun(this._cardPresentations)) {
      return deepClone(this._cardPresentations.cards ?? []);
    }
    return [];
  }

  get cardEvaluations(): JsonObject[] {
    return this.cardEvaluationsReady ? deepClone(this._cardEvaluations) : [];
  }

  get settings(): JsonObject[] {
    return deepClone(this._settings);
  }

  get modifierItems(): JsonObject {
    if (
      String(this._modifierItems.runId ?? '') === String(this._run.runId ?? '') &&
      Number(this._modifierItems.runSequence ?? -1) === Number(this._run.sequence ?? 0)
    ) {
      return deepClone(this._modifierItems);
    }
    return {};
  }

  get selectedSetting(): JsonObject {
    for (const item of this._settings) {
      if (String(item.settingId ?? '') === this.selectedSettingId) {
        return deepClone(item);
      }
    }
    return {};
  }

  get busy(): boolean {
    return this._operation !== 'idle';
  }

  get operation(): string {
    return this._operation;
  }

  get hasPendingCommand(): boolean {
    return Object.keys(this._pending).length > 0;
  }

  private belongsToCurrentRun(snapshot: JsonObject): boolean {
    return (
      String(snapshot.runId ?? '') === String(this._run.runId ?? '') &&
      Number(snapshot.runSequence ?? -1) === Number(this._run.sequence ?? 0) &&
      String(snapshot.contentRevision ?? '') === String(this._run.contentRevision ?? '')
    );
  }

  captureViewSnapshot(): JsonObject {
    const started = performance.now();
    const cards = this.belongsToCurrentRun(this._cardPresentations)
      ? (this._cardPresentations.cards ?? [])
      : [];
    const mods = this.modifierItems;
    const value = {
      run: this._run,
      combat: this._combat,
      actions: this._legalActions,
      combatActions: this._legalActions,
      zones: this._cardZones,
      combatCardZones: this._cardZones,
      evaluations: this.cardEvaluationsReady ? this._cardEvaluations : [],
      presentations: cards,
      combatCardPresentations: cards,
      activityCardPresentations: cards,
      activityModifierItems: Array.isArray(mods.items) ? mods.items : [],
      attributes: this.belongsToCurrentRun(this._characterAttributes)
        ? this._characterAttributes
        : {},
    };
    const result = deepClone(value);
    this.lastViewCaptureUsec = Math.round((performance.now() - started) * 1000);
    return result;
  }

  get engineOnline(): boolean {
    return this.available;
  }

  set engineOnline(value: boolean) {
    this.setAvailable(value);
  }

  hasActiveRun(): boolean {
    return (
      Object.keys(this._run).length > 0 &&
      Boolean(this._run.runId) &&
      String(this._run.lifecycle ?? 'active').toLowerCase() === 'active'
    );
  }

  hasActiveCombat(): boolean {
    return Object.keys(this._combat).length > 0 && Boolean(this._combat.combatId);
  }

  hasActiveDialogue(): boolean {
    return Array.isArray(this._run.dialogues) && this._run.dialogues.length > 0;
  }

  selectedSettingAllowsLoadout(): boolean {
    const s = this.selectedSetting;
    return Boolean(s.supportsLoadout ?? s.launch?.supportsLoadout);
  }

  async loadCatalog(): Promise<boolean> {
    const ok = await this.connectEngine(this.selectedSettingId);
    this.setAvailable(ok);
    this.changed.emit();
    return ok;
  }

  async startSelectedSetting(_assignments?: Record<string, string>): Promise<boolean> {
    const launch = this.selectedSetting.launch ?? {};
    const player = String(launch.playerEntityId ?? 'player');
    const mode = String(launch.modeId ?? 'default');
    const seed =
      typeof this._preferences?.nextCampaignSeed === 'function'
        ? this._preferences.nextCampaignSeed(player, this.selectedSettingId, mode)
        : Number(launch.seed ?? 20260911);
    const ok = await this.startCampaign(seed);
    if (ok && this._preferences && this._run.runId) {
      if (typeof this._preferences.saveRememberedRun === 'function') {
        this._preferences.saveRememberedRun(this._run);
      }
    }
    return ok;
  }

  async resumeRun(runId: string): Promise<boolean> {
    return this.continueRun(runId);
  }

  async startCustomRun(payload: JsonObject): Promise<boolean> {
    const modeId = String(payload.modeId ?? this.selectedSetting.launch?.modeId ?? 'sandbox');
    const scenario = isPlainObject(payload.scenario) ? payload.scenario : payload;
    const seed = Number(payload.seed ?? 20260911);
    return this.startSandbox(modeId, scenario, seed);
  }

  async ensureActivityCardPresentations(): Promise<JsonObject> {
    return this.loadCollectionPresentations();
  }

  async ensureActivityModifierItems(): Promise<JsonObject> {
    return { ok: true, data: this.modifierItems };
  }

  async fetchTimelinePage(cursor = 0, _limit = 50): Promise<JsonObject> {
    return this.timeline(cursor);
  }

  async fetchBranchTree(): Promise<JsonObject> {
    return this.branchTree();
  }

  async fetchHistoricalState(sequence: number): Promise<JsonObject> {
    return this.historicalState(sequence);
  }

  async createTimelineBranch(sequence: number, key: string): Promise<JsonObject> {
    return this.createBranch(sequence, key);
  }

  async activateBranch(runId: string): Promise<boolean> {
    return this.continueRun(runId);
  }

  async simulateCombat(cmd: JsonObject): Promise<JsonObject> {
    return this.simulate([cmd]);
  }

  async fetchProfileHistory(): Promise<JsonObject> {
    return this.runHistory();
  }

  async fetchCollection(category: string): Promise<JsonObject[]> {
    const res = await this.content(category, 200);
    if (!res.ok) return [];
    if (Array.isArray(res.data?.items)) return deepClone(res.data.items);
    if (Array.isArray(res.data)) return deepClone(res.data);
    return [];
  }

  configure(gateway: any): void {
    this._gateway = gateway;
  }

  setAvailable(value: boolean): void {
    this.available = value;
    this.availabilityChanged.emit(value);
  }

  async connectEngine(preferredSettingId = 'default'): Promise<boolean> {
    const ticket = this.begin('connecting');
    if (ticket < 0) return false;
    const ok = await this._connect(ticket, preferredSettingId);
    this.finish(ticket);
    return ok;
  }

  private async _connect(ticket: number, preferredSettingId = 'default'): Promise<boolean> {
    const response = await this._gateway.connectEngine();
    if (!this.current(ticket)) return false;
    if (!response.ok) {
      this.failed.emit(response);
      return false;
    }
    this._settings = Array.isArray(response.data?.items) ? deepClone(response.data.items) : [];
    if (this._settings.length === 0) {
      this.failed.emit({ errorKey: 'The engine did not publish any playable settings.' });
      return false;
    }
    let requested = preferredSettingId;
    if (!this.hasSetting(requested)) {
      requested = this.hasSetting('default')
        ? 'default'
        : String(this._settings[0]?.settingId ?? '');
    }
    return this.applySetting(requested, false);
  }

  selectSetting(settingId: string): boolean {
    if (this.busy || this.hasPendingCommand || !this.hasSetting(settingId)) {
      return false;
    }
    return this.applySetting(settingId, true);
  }

  private hasSetting(settingId: string): boolean {
    return this._settings.some((item) => String(item.settingId ?? '') === settingId);
  }

  private applySetting(settingId: string, clearActiveSession: boolean): boolean {
    for (const item of this._settings) {
      if (String(item.settingId ?? '') !== settingId) continue;
      this.selectedSettingId = settingId;
      this.contentRevision = String(item.currentRevision ?? '');
      if (clearActiveSession) {
        this._acceptedPresentation = {};
        this._run = {};
        this._combat = {};
        this._cardZones = {};
        this._characterAttributes = {};
        this._cardEvaluations = [];
        this._cardEvaluationContext = [];
        this._availableCommands = [];
        this._legalActions = [];
        this._toolCapabilities = {};
        this.synchronized = true;
      }
      this.changed.emit();
      return this.contentRevision.length > 0;
    }
    return false;
  }

  async startCampaign(seed: number): Promise<boolean> {
    return this.openRun('campaign', [seed]);
  }

  async startSandbox(modeId: string, scenario: JsonObject, seed: number): Promise<boolean> {
    return this.openRun('sandbox', [modeId, deepClone(scenario), seed]);
  }

  async continueRun(runId: string, expectedMode = ''): Promise<boolean> {
    if (!runId) return false;
    return this.openRun('continue', [runId, expectedMode]);
  }

  private async openRun(kind: string, args: any[]): Promise<boolean> {
    if (this.hasPendingCommand) return false;
    const ticket = this.begin('opening');
    if (ticket < 0) return false;
    if (
      kind !== 'continue' &&
      !this.contentRevision &&
      !(await this._connect(ticket, this.selectedSettingId))
    ) {
      this.finish(ticket);
      return false;
    }
    let response: JsonObject;
    if (kind === 'campaign') {
      response = await this._gateway.createCampaign(args[0], this.selectedSetting);
    } else if (kind === 'sandbox') {
      response = await this._gateway.createSandbox(
        args[0],
        args[1],
        args[2],
        this.contentRevision,
        this.selectedSettingId
      );
    } else {
      response = await this._gateway.readRun(args[0]);
    }
    if (!this.current(ticket)) return false;
    if (!response.ok) {
      this.failed.emit(response);
      this.finish(ticket);
      return false;
    }
    const opened: JsonObject = isPlainObject(response.data?.run)
      ? response.data.run
      : (response.data ?? {});
    if (kind === 'continue') {
      const launch = this.selectedSetting.launch ?? {};
      const settingId = String(opened.settingId ?? opened.configName ?? '');
      const valid =
        String(opened.runId ?? '') === String(args[0]) &&
        settingId === this.selectedSettingId &&
        this.hasSetting(settingId) &&
        String(opened.playerEntityId ?? '') === String(launch.playerEntityId ?? 'player') &&
        String(opened.lifecycle ?? '').toLowerCase() === 'active' &&
        (!String(args[1] ?? '') || String(opened.modeId ?? '') === String(args[1]));
      if (!valid) {
        this.failed.emit({
          errorKey:
            'This journey does not match the selected setting, player or mode, or is no longer active.',
        });
        this.finish(ticket);
        return false;
      }
    }
    this._run = deepClone(opened);
    this._acceptedPresentation = {};
    const openedSettingId = String(this._run.settingId ?? this.selectedSettingId);
    if (this.hasSetting(openedSettingId)) {
      this.selectedSettingId = openedSettingId;
    }
    this.contentRevision = String(this._run.contentRevision ?? this.contentRevision);
    this._combat = {};
    this._cardZones = {};
    this._characterAttributes = {};
    this._cardEvaluations = [];
    this._cardEvaluationContext = [];
    this.synchronized = false;
    const ok = await this._refresh(ticket);
    if (ok) {
      this.runOpened.emit(String(this._run.runId ?? ''));
    }
    this.finish(ticket);
    return ok;
  }

  async refresh(): Promise<boolean> {
    if (this.hasPendingCommand) {
      return this.recoverPending();
    }
    const ticket = this.begin('refreshing');
    if (ticket < 0) return false;
    const ok = await this._refresh(ticket);
    this.finish(ticket);
    return ok;
  }

  async previewTransformation(choice: JsonObject): Promise<JsonObject> {
    if (this.busy || !this.synchronized || Object.keys(this._run).length === 0) return {};
    const capturedSequence = Number(this._run.sequence ?? -1);
    const capturedRun = String(this._run.runId ?? '');
    const opMap: Record<string, string> = {
      UPGRADE_CARD: 'Apply',
      REMOVE_CARD_TRANSFORMATION: 'Remove',
      REPLACE_CARD_TRANSFORMATION: 'Replace',
    };
    const type = String(choice.type ?? '');
    const operation = opMap[type] ?? '';
    if (type === 'APPLY_CARD_MODIFIER_ITEM') {
      const itemPayload = choice.payload ?? {};
      const preview = await this._gateway.modifierItemPreview(
        capturedRun,
        String(itemPayload.itemInstanceId ?? ''),
        String(itemPayload.cardInstanceId ?? '')
      );
      if (
        !preview.ok ||
        capturedRun !== String(this._run.runId ?? '') ||
        capturedSequence !== Number(this._run.sequence ?? -1) ||
        Number(preview.data?.runSequence ?? -2) !== capturedSequence
      ) {
        return {};
      }
      const data = deepClone(preview.data);
      data.isCompatible = true;
      return data;
    }
    if (!operation) return {};
    const payload = choice.payload ?? {};
    const upgradeId =
      payload.upgradeId !== null && payload.upgradeId !== undefined
        ? String(payload.upgradeId)
        : '';
    const response = await this._gateway.transformationPreview(
      capturedRun,
      String(payload.cardInstanceId ?? ''),
      operation,
      upgradeId,
      payload.transformationId
    );
    if (
      !response.ok ||
      capturedRun !== String(this._run.runId ?? '') ||
      capturedSequence !== Number(this._run.sequence ?? -1)
    ) {
      return {};
    }
    if (Number(response.data?.expectedSequence ?? -2) !== capturedSequence) return {};
    return deepClone(response.data);
  }

  private async _refresh(
    ticket: number,
    committedCombat: JsonObject = {},
    receiptSequence = -1
  ): Promise<boolean> {
    if (Object.keys(this._run).length === 0) return false;
    const runId = String(this._run.runId ?? '');
    let response = await this._gateway.readSession(runId, receiptSequence);
    if (
      this.current(ticket) &&
      !response.ok &&
      Number(response.status ?? 0) === 409 &&
      receiptSequence >= 0
    ) {
      response = await this._gateway.readSession(runId, -1);
      receiptSequence = -1;
      committedCombat = {};
    }
    if (!this.current(ticket)) return false;
    if (!response.ok) return this.refreshFailed(response);
    if (!isPlainObject(response.data)) {
      return this.refreshFailed({
        ok: false,
        errorKey: 'The engine returned an invalid JSON response.',
      });
    }
    const data = response.data;
    if (!isPlainObject(data.run) || !isPlainObject(data.version)) {
      return this.refreshFailed({
        ok: false,
        errorKey: 'The engine returned an invalid JSON response.',
      });
    }
    const nextRun = data.run;
    const version = data.version;
    const nextCombat = isPlainObject(data.combat) ? data.combat : {};
    if (
      String(nextRun.runId ?? '') !== runId ||
      String(version.runId ?? '') !== runId ||
      Number(version.runSequence ?? -1) !== Number(nextRun.sequence ?? 0) ||
      String(version.settingId ?? '') !==
        String(nextRun.settingId ?? nextRun.configName ?? '') ||
      String(version.contentRevision ?? '') !== String(nextRun.contentRevision ?? '') ||
      Number(version.runStep ?? -1) !== Number(nextRun.step ?? 0) ||
      String(version.engineVersion ?? '') !== String(nextRun.engineVersion ?? '') ||
      String(version.stateFingerprint ?? '') !== String(nextRun.stateHash ?? '') ||
      !String(version.stateFingerprint ?? '') ||
      (receiptSequence >= 0 && Number(nextRun.sequence ?? -1) !== receiptSequence)
    ) {
      return this.refreshFailed({
        ok: false,
        errorKey: 'Could not refresh the game. Reconnect before playing again.',
      });
    }
    if (
      Object.keys(nextCombat).length > 0 &&
      (String(version.combatId ?? '') !== String(nextCombat.combatId ?? '') ||
        Number(version.combatStep ?? -1) !== Number(nextCombat.step ?? 0))
    ) {
      return this.refreshFailed({
        ok: false,
        errorKey: 'Could not refresh the game. Reconnect before playing again.',
      });
    }
    if (
      Object.keys(committedCombat).length > 0 &&
      receiptSequence >= 0 &&
      (String(committedCombat.combatId ?? '') !== String(nextCombat.combatId ?? '') ||
        Number(committedCombat.step ?? -1) !== Number(nextCombat.step ?? 0))
    ) {
      return this.refreshFailed({
        ok: false,
        errorKey: 'Presentation was superseded by a newer game revision.',
      });
    }

    const responses: Record<string, JsonObject> = {};
    for (const section of [
      'commands',
      'capabilities',
      'cardZones',
      'attributes',
      'cardPresentations',
      'modifierItems',
    ]) {
      if (!isPlainObject(data[section])) {
        return this.refreshFailed({
          ok: false,
          errorKey: 'The engine returned an invalid JSON response.',
        });
      }
      responses[section] = { ok: true, data: data[section] };
    }
    const refreshTimings = { session_loaded: Math.round(performance.now()) };
    if (!isPlainObject(data.actions) || !isPlainObject(data.evaluations)) {
      return this.refreshFailed({
        ok: false,
        errorKey: 'The engine returned an invalid JSON response.',
      });
    }
    if (
      !Array.isArray(data.actions.candidates) ||
      !Array.isArray(data.evaluations.cards) ||
      !Array.isArray(responses.commands.data.commands ?? []) ||
      !Array.isArray(responses.capabilities.data.granted ?? [])
    ) {
      return this.refreshFailed({
        ok: false,
        errorKey: 'The engine returned an invalid JSON response.',
      });
    }
    const nextActions: JsonObject[] = data.actions.candidates;
    const nextEvaluations: JsonObject[] = data.evaluations.cards;
    for (const card of nextEvaluations) {
      if (!isPlainObject(card) || !isPlainObject(card.version)) {
        return this.refreshFailed({
          ok: false,
          errorKey: 'The engine returned an invalid JSON response.',
        });
      }
      const inspectionVersion = card.version;
      if (
        Number(inspectionVersion.runSequence ?? -1) !== Number(nextRun.sequence ?? 0) ||
        String(inspectionVersion.runId ?? '') !== runId ||
        String(inspectionVersion.combatId ?? '') !== String(nextCombat.combatId ?? '') ||
        Number(inspectionVersion.combatStep ?? -1) !== Number(nextCombat.step ?? 0) ||
        String(inspectionVersion.contentRevision ?? '') !== String(nextRun.contentRevision ?? '')
      ) {
        return this.refreshFailed({
          ok: false,
          errorKey: 'Could not refresh the game. Reconnect before playing again.',
        });
      }
    }
    const nextAttributes = responses.attributes?.data ?? {};
    if (Object.keys(nextAttributes).length > 0) {
      if (
        String(nextAttributes.runId ?? '') !== runId ||
        Number(nextAttributes.runSequence ?? -1) !== Number(nextRun.sequence ?? 0) ||
        String(nextAttributes.contentRevision ?? '') !== String(nextRun.contentRevision ?? '')
      ) {
        return this.refreshFailed({
          ok: false,
          errorKey: 'Could not refresh the game. Reconnect before playing again.',
        });
      }
      if (
        Object.keys(nextCombat).length > 0 &&
        (String(nextAttributes.combatId ?? '') !== String(nextCombat.combatId ?? '') ||
          Number(nextAttributes.combatStep ?? -1) !== Number(nextCombat.step ?? 0))
      ) {
        return this.refreshFailed({
          ok: false,
          errorKey: 'Could not refresh the game. Reconnect before playing again.',
        });
      }
    }
    const nextPresentations = responses.cardPresentations?.data ?? {};
    if (
      Object.keys(nextPresentations).length > 0 &&
      (String(nextPresentations.runId ?? '') !== runId ||
        Number(nextPresentations.runSequence ?? -1) !== Number(nextRun.sequence ?? 0) ||
        String(nextPresentations.contentRevision ?? '') !== String(nextRun.contentRevision ?? ''))
    ) {
      return this.refreshFailed({
        ok: false,
        errorKey: 'Could not refresh the game. Reconnect before playing again.',
      });
    }
    const nextItems = responses.modifierItems?.data ?? {};
    if (
      Object.keys(nextItems).length > 0 &&
      (String(nextItems.runId ?? '') !== runId ||
        Number(nextItems.runSequence ?? -1) !== Number(nextRun.sequence ?? 0) ||
        String(nextItems.contentRevision ?? '') !== String(nextRun.contentRevision ?? ''))
    ) {
      return this.refreshFailed({
        ok: false,
        errorKey: 'Could not refresh the game. Reconnect before playing again.',
      });
    }

    this._modifierItems = deepClone(nextItems);
    this._cardPresentations = deepClone(nextPresentations);
    this._characterAttributes = deepClone(nextAttributes);
    this._run = deepClone(nextRun);
    this._combat = deepClone(nextCombat);
    this._cardZones = deepClone(responses.cardZones.data);
    this._availableCommands = deepClone(responses.commands.data.commands ?? []);
    this._legalActions = deepClone(nextActions);
    this._cardEvaluations = deepClone(nextEvaluations);
    this._cardEvaluationContext = this.evaluationContext(this._run, this._combat);
    this._toolCapabilities = {};
    this.toolProfile = String(responses.capabilities.data.profile ?? 'normal');
    for (const capability of responses.capabilities.data.granted ?? []) {
      this._toolCapabilities[String(capability)] = true;
    }
    this.synchronized = true;
    this.changed.emit();

    if (Object.keys(this._acceptedPresentation).length > 0) {
      const accepted = this._acceptedPresentation;
      this._acceptedPresentation = {};
      const envelope = PresentationEnvelope.build(
        accepted.receipt,
        accepted.before,
        this.captureViewSnapshot(),
        accepted.endpoint
      );
      if (envelope !== null) {
        for (const [marker, ts] of Object.entries(refreshTimings)) {
          envelope.markAt(marker, ts);
        }
        envelope.markAt('submitted', Number(accepted.submitted ?? 0));
        envelope.markAt('received', Number(accepted.received ?? 0));
        this.presentationReady.emit(envelope);
      } else {
        this.presentationDiscarded.emit();
        this.failed.emit({
          errorKey: 'Presentation was superseded by a newer game revision.',
        });
      }
    }
    return true;
  }

  private refreshFailed(error: JsonObject): boolean {
    this.synchronized = false;
    this._legalActions = [];
    this._availableCommands = [];
    this._toolCapabilities = {};
    this.toolProfile = 'normal';
    this.failed.emit(error);
    this.changed.emit();
    return false;
  }

  private evaluationContext(runState: JsonObject, combatState: JsonObject): any[] {
    return [
      this.selectedSettingId,
      runState.runId ?? '',
      runState.branchId ?? '',
      runState.sequence ?? -1,
      runState.contentRevision ?? '',
      combatState.combatId ?? '',
      combatState.step ?? -1,
      GameSession.resolveInputActorId(combatState),
    ];
  }

  async executeRunCommand(type: string, payload: JsonObject, label = ''): Promise<boolean> {
    const advertised = this.command(type);
    const expectedStep = Number(advertised.expectedStep ?? this._run.step ?? 0);
    return this.execute(
      'runs',
      String(this._run.runId ?? ''),
      type,
      payload,
      label,
      expectedStep
    );
  }

  async readDeckCompartments(): Promise<JsonObject> {
    if (this.busy || this.hasPendingCommand || Object.keys(this._run).length === 0) {
      return { ok: false, error: 'Open a run and recover pending commands first.' };
    }
    const ticket = this._generation;
    const response = await this._gateway.readDeckCompartments(String(this._run.runId));
    if (ticket !== this._generation) {
      return { ok: false, error: 'Session changed while reading compartments.' };
    }
    if (response.ok) {
      if (
        !this.loadoutContextMatches(response.data) ||
        String(response.data?.actorId ?? '') !== String(this._run.playerEntityId ?? '')
      ) {
        return {
          ok: false,
          status: 409,
          error: 'Compartment context changed. Reopen the editor.',
        };
      }
      response.data._clientContext = this.replayContext();
    }
    return response;
  }

  async previewDeckCompartments(snapshot: JsonObject, allocations: any[]): Promise<JsonObject> {
    if (this.busy || this.hasPendingCommand || !this.loadoutContextMatches(snapshot)) {
      return {
        ok: false,
        status: 409,
        error: 'Compartment context changed. Reopen the editor.',
      };
    }
    const response = await this._gateway.previewDeckCompartments(snapshot, allocations);
    if (!this.loadoutContextMatches(snapshot)) {
      return { ok: false, status: 409, error: 'Session changed while previewing.' };
    }
    return response;
  }

  async applyDeckCompartments(snapshot: JsonObject, allocations: any[]): Promise<boolean> {
    if (
      !this.loadoutContextMatches(snapshot) ||
      String(snapshot.actorId ?? '') !== String(this._run.playerEntityId ?? '')
    ) {
      return false;
    }
    return this.execute(
      'runs',
      String(snapshot.runId),
      'APPLY_DECK_COMPARTMENTS',
      {
        actorId: snapshot.actorId,
        allocations: deepClone(allocations),
      },
      'Apply deck compartments',
      Number(snapshot.expectedStep ?? 0)
    );
  }

  loadoutContextMatches(snapshot: JsonObject): boolean {
    if (!isPlainObject(snapshot)) return false;
    return (
      this.synchronized &&
      Object.keys(this._run).length > 0 &&
      String(snapshot.runId ?? '') === String(this._run.runId ?? '') &&
      Number(snapshot.runSequence ?? -1) === Number(this._run.sequence ?? 0) &&
      Number(snapshot.expectedStep ?? -1) === Number(this._run.step ?? 0) &&
      String(snapshot.contentRevision ?? '') === String(this._run.contentRevision ?? '') &&
      deepEqual(snapshot._clientContext ?? this.replayContext(), this.replayContext())
    );
  }

  async readLoadout(actorId: string): Promise<JsonObject> {
    if (this.busy || this.hasPendingCommand || Object.keys(this._run).length === 0) {
      return { ok: false, error: 'Open a run and recover pending commands first.' };
    }
    const ticket = this._generation;
    const response = await this._gateway.readLoadout(String(this._run.runId), actorId);
    if (ticket !== this._generation) {
      return { ok: false, error: 'Session changed while reading loadout.' };
    }
    if (
      response.ok &&
      (!this.loadoutContextMatches(response.data) ||
        String(response.data?.actorId ?? '') !== actorId)
    ) {
      return { ok: false, error: 'Loadout changed. Refresh before editing.' };
    }
    if (response.ok && isPlainObject(response.data)) {
      response.data._clientContext = this.replayContext();
    }
    return response;
  }

  async previewLoadout(snapshot: JsonObject, assignments: JsonObject): Promise<JsonObject> {
    if (this.busy || this.hasPendingCommand || !this.loadoutContextMatches(snapshot)) {
      return { ok: false, error: 'Loadout changed. Refresh before editing.' };
    }
    const response = await this._gateway.previewLoadout(snapshot, assignments);
    if (!this.loadoutContextMatches(snapshot)) {
      return { ok: false, error: 'Loadout changed while previewing.' };
    }
    return response;
  }

  async applyLoadout(snapshot: JsonObject, assignments: JsonObject): Promise<boolean> {
    if (!this.loadoutContextMatches(snapshot)) return false;
    return this.execute(
      'runs',
      String(snapshot.runId),
      'APPLY_LOADOUT',
      {
        actorId: snapshot.actorId,
        assignments: deepClone(assignments),
      },
      'Apply inscriptions',
      Number(snapshot.expectedStep ?? 0)
    );
  }

  async executeCombatCommand(type: string, payload: JsonObject, label = ''): Promise<boolean> {
    return this.execute(
      'combats',
      String(this._combat.combatId ?? ''),
      type,
      payload,
      label,
      Number(this._combat.step ?? this._run.step ?? 0)
    );
  }

  async submitCandidate(candidate: JsonObject): Promise<boolean> {
    const commandData = candidate.command ?? {};
    const type =
      String(candidate.source ?? '') === 'Card'
        ? 'PLAY_CARD'
        : String(commandData.actionType ?? '') === 'END_TURN'
          ? 'END_TURN'
          : 'EXECUTE_ACTION';
    const payload =
      typeof this._gateway.candidatePayload === 'function'
        ? this._gateway.candidatePayload(commandData)
        : deepClone(commandData);
    return this.executeCombatCommand(type, payload);
  }

  private async execute(
    scope: string,
    id: string,
    type: string,
    payload: JsonObject,
    label: string,
    expectedStep: number
  ): Promise<boolean> {
    if (!this.synchronized || Object.keys(this._run).length === 0 || this.hasPendingCommand) {
      return false;
    }
    const ticket = this.begin('submitting');
    if (ticket < 0) return false;
    this.commandStarted.emit(label.length > 0 ? label : type);
    this._commandStartedMs = Math.round(performance.now());
    this._pending = this._gateway.prepareCommand(
      scope,
      id,
      type,
      payload,
      Number(this._run.sequence ?? 0),
      expectedStep
    );
    return this.sendPending(ticket);
  }

  async recoverPending(): Promise<boolean> {
    if (!this.hasPendingCommand) {
      return this.refresh();
    }
    const ticket = this.begin('recovering');
    if (ticket < 0) return false;
    return this.sendPending(ticket);
  }

  private async sendPending(ticket: number): Promise<boolean> {
    let response = await this._gateway.sendPrepared(this._pending);
    if (!this.current(ticket)) return false;
    if (
      response.ok &&
      (!isPlainObject(response.data) || typeof response.data.sequence !== 'number')
    ) {
      response = {
        ok: false,
        status: 0,
        errorKey: 'The engine returned an invalid JSON response.',
      };
    }
    if (!response.ok) {
      const status = Number(response.status ?? 0);
      if (status > 0 && status < 500) {
        this._pending = {};
      }
      this.synchronized = false;
      this._legalActions = [];
      this._availableCommands = [];
      this.failed.emit(response);
      this.finish(ticket);
      this.commandFinished.emit();
      this.changed.emit();
      return false;
    }
    this._pending = {};
    this._acceptedPresentation = {
      receipt: deepClone(response.data),
      before: { run: this.run, combat: this.combat, zones: this.cardZones },
      endpoint:
        typeof this._gateway.replayContext === 'function'
          ? this._gateway.replayContext()
          : ['fixture', this._generation],
      submitted: this._commandStartedMs,
      received: Math.round(performance.now()),
    };
    this.receiptReceived.emit(deepClone(response.data));
    const state = PresentationEnvelope.dictionary(response.data?.state);
    const updated = await this._refresh(
      ticket,
      isPlainObject(state.combat) ? state.combat : {},
      Number(response.data?.sequence ?? -1)
    );
    this.finish(ticket);
    this.commandFinished.emit();
    if (!updated && this.current(ticket)) {
      this.failed.emit({
        errorKey: 'Could not refresh the game. Reconnect before playing again.',
      });
    }
    return true;
  }

  private begin(state: string): number {
    if (this.busy) return -1;
    this._generation += 1;
    this._operation = state;
    this.operationChanged.emit(state);
    return this._generation;
  }

  private current(ticket: number): boolean {
    return ticket === this._generation;
  }

  private finish(ticket: number): void {
    if (this.current(ticket)) {
      this._operation = 'idle';
      this.operationChanged.emit(this._operation);
    }
  }

  invalidate(): void {
    if (this.hasPendingCommand) return;
    this._generation += 1;
    this._operation = 'idle';
    this._run = {};
    this._acceptedPresentation = {};
    this._combat = {};
    this._cardZones = {};
    this._characterAttributes = {};
    this._cardEvaluations = [];
    this._cardEvaluationContext = [];
    this._legalActions = [];
    this._availableCommands = [];
    this._toolCapabilities = {};
    this.toolProfile = 'normal';
    this.contentRevision = '';
    this._settings = [];
    this.synchronized = false;
    this.setAvailable(false);
    this.operationChanged.emit(this._operation);
    this.changed.emit();
  }

  inputActorId(): string {
    return GameSession.resolveInputActorId(this._combat);
  }

  static resolveInputActorId(snapshot: JsonObject): string {
    if (!isPlainObject(snapshot) || Object.keys(snapshot).length === 0) return '';
    const priority = snapshot.priorityWindow;
    if (isPlainObject(priority) && Object.keys(priority).length > 0) {
      return String(priority.holderActorId ?? '');
    }
    const activation = snapshot.activation;
    if (isPlainObject(activation) && Boolean(activation.waitingForInput)) {
      return String(activation.activeActorId ?? snapshot.activeActorId ?? '');
    }
    return String(snapshot.activeActorId ?? '');
  }

  command(type: string): JsonObject {
    for (const candidate of this.availableCommands) {
      if (String(candidate.type ?? '') === type) {
        return candidate;
      }
    }
    return {};
  }

  allowsTool(capability: string): boolean {
    return Boolean(this._toolCapabilities[capability]);
  }

  private toolDenied(capability: string): JsonObject {
    return {
      ok: false,
      status: 403,
      errorKey: 'This tool is not available in the current mode.',
      error: `Tool capability denied: ${capability}`,
    };
  }

  async timeline(afterSequence = 0): Promise<JsonObject> {
    if (!this.allowsTool('timeline.read')) {
      return this.toolDenied('timeline.read');
    }
    if (Object.keys(this._combat).length === 0) {
      return { ok: false, errorKey: 'No active combat.', error: 'No active combat.' };
    }
    const maxItems = Number(
      this._run.resolvedMode?.timelinePolicy?.maxItemsPerPage ?? 50
    );
    const limit = Math.min(50, maxItems);
    return this._gateway.timeline(String(this._combat.combatId ?? ''), afterSequence, limit);
  }

  async branchTree(): Promise<JsonObject> {
    if (!this.allowsTool('timeline.branch.read')) {
      return this.toolDenied('timeline.branch.read');
    }
    return this._gateway.branchTree(String(this._run.runId ?? ''));
  }

  async createBranch(sequence: number, key: string): Promise<JsonObject> {
    if (!this.allowsTool('timeline.branch.create')) {
      return this.toolDenied('timeline.branch.create');
    }
    if (Object.keys(this._combat).length === 0) {
      return { ok: false, errorKey: 'No active combat.', error: 'No active combat.' };
    }
    if (this.hasPendingCommand) {
      return { ok: false, errorKey: 'Recover the pending command first.' };
    }
    const ticket = this.begin('branching');
    if (ticket < 0) {
      return { ok: false, errorKey: 'An operation is already in progress.' };
    }
    const result = await this._gateway.createBranch(
      String(this._combat.combatId ?? ''),
      sequence,
      key
    );
    this.finish(ticket);
    return result;
  }

  async verify(): Promise<JsonObject> {
    if (!this.allowsTool('replay.verify')) {
      return this.toolDenied('replay.verify');
    }
    return this.verifyRun(String(this._run.runId ?? ''));
  }

  async verifyRun(runId: string): Promise<JsonObject> {
    if (!runId) {
      return {
        ok: false,
        errorKey: 'No journey was selected.',
        error: 'No journey was selected.',
      };
    }
    return this._gateway.verify(runId);
  }

  async runHistory(): Promise<JsonObject> {
    const setting = this.selectedSettingId;
    const player = String(this.selectedSetting.launch?.playerEntityId ?? 'player');
    const response = await this._gateway.runHistory(player, setting);
    if (this.selectedSettingId !== setting) {
      return { ok: false, errorKey: 'The selected setting changed. Try again.' };
    }
    return response;
  }

  async findResume(
    index: RunResumeIndex,
    api: string,
    legacyId = ''
  ): Promise<JsonObject> {
    const setting = this.selectedSettingId;
    const launch = this.selectedSetting.launch ?? {};
    const player = String(launch.playerEntityId ?? 'player');
    const mode = String(launch.modeId ?? '');
    const generation = this._generation;
    const ids: string[] = [];
    const bookmarked = index.candidate(api, player, setting, mode);
    if (!bookmarked && index.hasScope(api, player, setting, mode)) {
      return { ok: true, runId: '', legacyChecked: false };
    }
    if (bookmarked) ids.push(bookmarked);
    if (legacyId && !ids.includes(legacyId)) ids.push(legacyId);
    let legacyChecked = false;

    for (const id of ids) {
      const response = await this._gateway.readRun(id);
      if (generation !== this._generation || this.selectedSettingId !== setting) {
        return { ok: false, errorKey: 'The selected setting changed. Try again.' };
      }
      if (!response.ok) {
        if (Number(response.status ?? 0) !== 404) return response;
        index.forget(api, player, setting, mode, id);
        if (index.hasScope(api, player, setting, mode)) {
          return { ok: true, runId: '', legacyChecked };
        }
        continue;
      }
      const record: JsonObject = response.data ?? {};
      if (String(record.runId ?? '') !== id) {
        return {
          ok: false,
          errorKey:
            'This journey does not match the selected setting, player or mode, or is no longer active.',
        };
      }
      if (id === legacyId) legacyChecked = true;
      index.remember(api, record);
      if (
        String(record.settingId ?? record.configName ?? '') === setting &&
        String(record.playerEntityId ?? '') === player &&
        String(record.modeId ?? '') === mode &&
        String(record.lifecycle ?? '').toLowerCase() === 'active'
      ) {
        return { ok: true, runId: id, legacyChecked };
      }
      index.forget(api, player, setting, mode, id);
      if (
        index.hasScope(api, player, setting, mode) &&
        !index.candidate(api, player, setting, mode)
      ) {
        return { ok: true, runId: '', legacyChecked };
      }
    }

    const profile = await this._gateway.runHistory(player, setting);
    if (generation !== this._generation || this.selectedSettingId !== setting) {
      return { ok: false, errorKey: 'The selected setting changed. Try again.' };
    }
    if (!profile.ok) return profile;
    for (const record of profile.data?.items ?? []) {
      if (
        String(record.settingId ?? record.configName ?? '') !== setting ||
        String(record.modeId ?? '') !== mode ||
        String(record.lifecycle ?? '').toLowerCase() !== 'active'
      ) {
        continue;
      }
      const metadata = deepClone(record);
      metadata.playerEntityId = player;
      index.remember(api, metadata);
      return { ok: true, runId: String(record.runId ?? ''), legacyChecked };
    }
    return { ok: true, runId: '', legacyChecked };
  }

  async replayTimeline(
    runId: string,
    afterSequence = 0,
    limit = 25,
    throughSequence = 0
  ): Promise<JsonObject> {
    if (!runId) {
      return {
        ok: false,
        errorKey: 'No journey was selected.',
        error: 'No journey was selected.',
      };
    }
    return this._gateway.runTimeline(runId, afterSequence, limit, throughSequence);
  }

  replayContext(): any[] {
    return typeof this._gateway?.replayContext === 'function'
      ? this._gateway.replayContext()
      : ['fixture', this._generation];
  }

  beginReplayPage(
    runId: string,
    afterSequence: number,
    limit: number,
    throughSequence: number
  ): ReadTicket {
    return this._gateway.beginRunTimeline(runId, afterSequence, limit, throughSequence);
  }

  beginReplayPresentation(runId: string, sequence: number): ReadTicket {
    return this._gateway.beginRunPresentation(runId, sequence);
  }

  beginReplayInspection(runId: string, sequence: number): ReadTicket {
    return this._gateway.beginRunCommit(runId, sequence);
  }

  async replayCommit(runId: string, sequence: number): Promise<JsonObject> {
    if (!runId || sequence < 1) {
      return {
        ok: false,
        errorKey: 'No replay command was selected.',
        error: 'No replay command was selected.',
      };
    }
    return this._gateway.runCommit(runId, sequence);
  }

  async simulate(commands: any[]): Promise<JsonObject> {
    if (!this.allowsTool('simulation.run')) {
      return this.toolDenied('simulation.run');
    }
    return this._gateway.simulate(
      String(this._run.runId ?? ''),
      Number(this._run.sequence ?? 0),
      commands
    );
  }

  async simulateEndTurn(): Promise<JsonObject> {
    for (const candidate of this._legalActions) {
      if (String(candidate.command?.actionType ?? '') === 'END_TURN') {
        const payload =
          typeof this._gateway.candidatePayload === 'function'
            ? this._gateway.candidatePayload(candidate.command)
            : deepClone(candidate.command);
        return this.simulate([{ type: 'END_TURN', payload }]);
      }
    }
    return {
      ok: false,
      errorKey: 'No actor is waiting for input to simulate.',
      error: 'No actor is waiting for input to simulate.',
    };
  }

  async content(kind: string, limit = 100): Promise<JsonObject> {
    return this._gateway.content(
      kind,
      this.contentRevision,
      this.selectedSettingId,
      limit
    );
  }

  activityChoices(): JsonObject[] {
    return ActivityChoices.build(this._run, this._availableCommands);
  }

  async submitActivity(choice: JsonObject): Promise<boolean> {
    return this.executeRunCommand(String(choice.type ?? ''), choice.payload ?? {});
  }

  async inspectHand(targetIds: string[] = []): Promise<JsonObject> {
    return this._gateway.inspectHand(
      String(this._combat.combatId ?? ''),
      this.inputActorId(),
      targetIds
    );
  }

  async inspectCard(instanceId: string): Promise<JsonObject> {
    return this._gateway.inspectCard(
      String(this._combat.combatId ?? ''),
      this.inputActorId(),
      instanceId
    );
  }

  async loadCollectionPresentations(): Promise<JsonObject> {
    if (!this.synchronized || this.busy || Object.keys(this._run).length === 0) {
      return { ok: false };
    }
    const identity = [
      this._run.runId,
      this._run.sequence,
      this._run.contentRevision,
      this._run.stateHash,
    ];
    if (deepEqual(this._collectionPresentations.identity ?? [], identity)) {
      return { ok: true, data: deepClone(this._collectionPresentations.data) };
    }
    const response = await this._gateway.readCardPresentations(String(this._run.runId));
    const currentIdentity = [
      this._run.runId,
      this._run.sequence,
      this._run.contentRevision,
      this._run.stateHash,
    ];
    if (!deepEqual(identity, currentIdentity)) {
      return { ok: false, cancelled: true };
    }
    if (!response.ok) return response;
    const data = response.data ?? {};
    if (
      String(data.runId ?? '') !== String(this._run.runId) ||
      Number(data.runSequence ?? -1) !== Number(this._run.sequence) ||
      String(data.contentRevision ?? '') !== String(this._run.contentRevision ?? '')
    ) {
      return { ok: false, cancelled: true };
    }
    this._collectionPresentations = { identity, data: deepClone(data) };
    return { ok: true, data: deepClone(data) };
  }

  async historicalState(sequence: number): Promise<JsonObject> {
    if (!this.allowsTool('timeline.inspect_state')) {
      return this.toolDenied('timeline.inspect_state');
    }
    return this._gateway.historicalState(String(this._combat.combatId ?? ''), sequence);
  }

  async resolution(commandId: string): Promise<JsonObject> {
    return this._gateway.resolution(String(this._combat.combatId ?? ''), commandId);
  }
}
