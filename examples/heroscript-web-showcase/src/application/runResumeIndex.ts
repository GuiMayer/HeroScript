import { deepClone, isPlainObject, JsonObject } from '../contracts/types';

export interface ConfigStore {
  getValue(section: string, key: string, defaultValue?: any): any;
  setValue(section: string, key: string, value: any): void;
  hasSectionKey?(section: string, key: string): boolean;
}

/**
 * Local bookmarks only. Authoritative lifecycle, rules and permanent progress stay in REST.
 * Port of scripts/application/run_resume_index.gd
 */
export class RunResumeIndex {
  runs: Record<string, string> = {};
  counters: Record<string, number> = {};

  static key(api: string, player: string, setting: string, mode: string): string {
    return JSON.stringify([api.replace(/\/+$/, ''), player, setting, mode]);
  }

  candidate(api: string, player: string, setting: string, mode: string): string {
    return String(this.runs[RunResumeIndex.key(api, player, setting, mode)] ?? '');
  }

  runFor(
    api: string,
    player: string,
    setting: string,
    mode: string,
    fallback = ''
  ): string {
    const scope = RunResumeIndex.key(api, player, setting, mode);
    if (Object.prototype.hasOwnProperty.call(this.runs, scope)) {
      return String(this.runs[scope] ?? '');
    }
    return fallback;
  }

  hasScope(api: string, player: string, setting: string, mode: string): boolean {
    return Object.prototype.hasOwnProperty.call(
      this.runs,
      RunResumeIndex.key(api, player, setting, mode)
    );
  }

  remember(
    api: string,
    runOrPlayer: JsonObject | string,
    settingArg?: string,
    modeArg?: string,
    runIdArg?: string
  ): boolean {
    if (typeof runOrPlayer === 'string') {
      const player = runOrPlayer;
      const setting = String(settingArg ?? '');
      const mode = String(modeArg ?? '');
      const id = String(runIdArg ?? '');
      if (!setting || !player || !mode) return false;
      const scope = RunResumeIndex.key(api, player, setting, mode);
      this.runs[scope] = id;
      return true;
    }
    const run = runOrPlayer;
    const setting = String(run.settingId ?? run.configName ?? '');
    const player = String(run.playerEntityId ?? '');
    const mode = String(run.modeId ?? '');
    const id = String(run.runId ?? '');
    if (!setting || !player || !mode || !id) return false;
    const scope = RunResumeIndex.key(api, player, setting, mode);
    if (String(run.lifecycle ?? '').toLowerCase() === 'active') {
      if (String(this.runs[scope] ?? '') === id) return false;
      this.runs[scope] = id;
      return true;
    }
    if (!Object.prototype.hasOwnProperty.call(this.runs, scope) || String(this.runs[scope] ?? '') === id) {
      // A known terminal bookmark must not resurrect an older unfinished run.
      this.runs[scope] = '';
      return true;
    }
    return false;
  }

  forget(api: string, player: string, setting: string, mode: string, id: string): void {
    const scope = RunResumeIndex.key(api, player, setting, mode);
    if (String(this.runs[scope] ?? '') === id) {
      this.runs[scope] = '';
    }
  }

  forgetRun(api: string, runId: string): void {
    const normalizedApi = api.replace(/\/+$/, '');
    for (const [scope, id] of Object.entries(this.runs)) {
      if (id === runId && scope.startsWith(`["${normalizedApi}"`)) {
        this.runs[scope] = '';
      }
    }
  }

  nextCounter(
    api: string,
    player: string,
    setting: string,
    mode: string,
    legacyFloor: number
  ): number {
    const scope = RunResumeIndex.key(api, player, setting, mode);
    const current = Object.prototype.hasOwnProperty.call(this.counters, scope)
      ? Number(this.counters[scope])
      : legacyFloor;
    this.counters[scope] = current + 1;
    return this.counters[scope];
  }

  loadData(savedRuns: any, savedCounters: any): void {
    this.runs = isPlainObject(savedRuns) ? deepClone(savedRuns) : {};
    this.counters = isPlainObject(savedCounters) ? deepClone(savedCounters) : {};
  }

  loadConfig(config: ConfigStore): void {
    const savedRuns = config.getValue('session', 'resume_runs', {});
    const savedCounters = config.getValue('session', 'campaign_counters', {});
    this.loadData(savedRuns, savedCounters);
  }

  writeConfig(config: ConfigStore): void {
    config.setValue('session', 'resume_runs', deepClone(this.runs));
    config.setValue('session', 'campaign_counters', deepClone(this.counters));
  }

  toJSON(): { resume_runs: Record<string, string>; campaign_counters: Record<string, number> } {
    return {
      resume_runs: deepClone(this.runs),
      campaign_counters: deepClone(this.counters),
    };
  }
}
