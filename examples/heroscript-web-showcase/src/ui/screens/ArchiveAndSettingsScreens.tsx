import React, { useEffect, useMemo, useRef, useState } from "react";
import { Preferences, SETTINGS_CATEGORIES } from "../../application/preferences";
import { AppServices, ScreenId } from "../../bootstrap";
import { isPlainObject, JsonObject } from "../../contracts/types";
import tagSynergyScenarios from "../../data/labs/tag_synergy_scenarios.json";
import { TimelinePresenter } from "../../presentation/timelineAndReplayPresenters";
import { RunReplayController, SettingsController } from "../controllers/controllers";

export const TimelineScreen: React.FC<{
  services: AppServices;
  onNavigate: (screen: ScreenId) => void;
}> = ({ services, onNavigate }) => {
  const { session, i18n, audio } = services;
  const [presenter] = useState(() => new TimelinePresenter());
  const [entries, setEntries] = useState<JsonObject[]>([]);
  const [lineage, setLineage] = useState<JsonObject[]>([]);
  const [selectedSeq, setSelectedSeq] = useState<number>(-1);
  const [inspectedState, setInspectedState] = useState<string>("");
  const [branchName, setBranchName] = useState<string>("");
  const [simResult, setSimResult] = useState<string>("");

  const loadTimeline = async () => {
    const page = await session.fetchTimelinePage(presenter.nextCursor, 50);
    presenter.appendPage(page);
    setEntries(presenter.entries());
    const tree = await session.fetchBranchTree();
    if (Object.keys(tree).length > 0) {
      setLineage(TimelinePresenter.lineage(tree));
    }
  };

  useEffect(() => {
    if (session.hasActiveRun()) {
      loadTimeline();
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [session.run.runId, session.run.sequence]);

  const handleInspectSequence = async (seq: number) => {
    audio.ui();
    setSelectedSeq(seq);
    const state = await session.fetchHistoricalState(seq);
    setInspectedState(JSON.stringify(state, null, 2));
  };

  return (
    <div className="flex flex-col h-full w-full ivory-surface overflow-hidden">
      <header className="flex flex-wrap items-center justify-between gap-3 px-6 py-3 bg-[#eee4d1] border-b-2 border-[#20201c]">
        <div>
          <span className="text-[10px] font-display font-semibold uppercase tracking-widest text-[#b83e25]">
            CAUSAL COMMAND JOURNAL
          </span>
          <h2 className="font-display text-lg font-bold m-0 text-[#20201c]">
            {i18n.text("TIMELINE & BRANCHES")}
          </h2>
        </div>

        <div className="flex items-center gap-2">
          {session.hasActiveCombat() && (
            <button
              type="button"
              onClick={async () => {
                const sim = await session.simulateCombat({
                  actionType: "END_TURN",
                });
                setSimResult(JSON.stringify(sim, null, 2));
              }}
              className="ivory-button px-3 py-1 text-xs rounded"
            >
              {i18n.text("SIMULATE END TURN")}
            </button>
          )}
          <button
            type="button"
            onClick={() =>
              onNavigate(session.hasActiveCombat() ? "combat" : "journey")
            }
            className="ivory-button px-3.5 py-1 text-xs rounded"
          >
            {i18n.text("BACK")}
          </button>
        </div>
      </header>

      <div className="flex-1 overflow-y-auto p-6 grid grid-cols-1 lg:grid-cols-[360px_1fr] gap-6 items-start">
        {/* Left: Recorded Commands + Branch Lineage */}
        <div className="space-y-4">
          <div className="ivory-panel rounded-md p-4 space-y-2.5">
            <h3 className="font-display text-sm font-bold m-0">
              {i18n.text("RECORDED COMMANDS")} ({entries.length})
            </h3>
            <div className="max-h-[340px] overflow-y-auto space-y-1.5 pr-1">
              {entries.map((entry) => {
                const seq = Number(entry.runSequence ?? entry.sequence ?? 0);
                const isSelected = selectedSeq === seq;
                return (
                  <button
                    key={seq}
                    type="button"
                    onClick={() => handleInspectSequence(seq)}
                    className={`w-full text-left p-2.5 rounded border text-xs flex items-center justify-between ${
                      isSelected
                        ? "bg-[#20201c] text-[#faf5eb] border-[#20201c]"
                        : "bg-[#faf5eb] text-[#20201c] border-[#8d8270]/60"
                    }`}
                  >
                    <span className="font-mono font-bold">#{seq}</span>
                    <span className="truncate ml-2 flex-1">
                      {String(entry.commandType ?? entry.kind ?? "COMMAND")}
                    </span>
                  </button>
                );
              })}
            </div>
          </div>

          {/* Branching Controls */}
          <div className="ivory-panel rounded-md p-4 space-y-3 text-xs">
            <h3 className="font-display text-sm font-bold m-0">
              {i18n.text("CREATE TIMELINE BRANCH")}
            </h3>
            <input
              type="text"
              placeholder={i18n.text("Branch key (optional)…")}
              value={branchName}
              onChange={(e) => setBranchName(e.target.value)}
              className="w-full p-2 rounded border border-[#8d8270] bg-[#faf5eb]"
            />
            <button
              type="button"
              disabled={selectedSeq < 0 || session.busy}
              onClick={async () => {
                const ok = await session.createTimelineBranch(
                  selectedSeq,
                  branchName
                );
                if (ok) {
                  onNavigate(session.hasActiveCombat() ? "combat" : "journey");
                }
              }}
              className="w-full ivory-button-primary py-2 rounded"
            >
              {i18n.text("BRANCH FROM SEQUENCE")} #{selectedSeq >= 0 ? selectedSeq : "—"}
            </button>

            {lineage.length > 0 && (
              <div className="pt-2 border-t border-[#8d8270]/40 space-y-1">
                <div className="font-display font-bold">
                  {i18n.text("LINEAGE TREE")}
                </div>
                {lineage.map((row, idx) => (
                  <div
                    key={idx}
                    className="flex items-center justify-between py-1"
                    style={{ paddingLeft: `${Number(row.depth ?? 0) * 12}px` }}
                  >
                    <span className="font-mono truncate">
                      {String(row.branchKey || row.runId)}
                    </span>
                    <button
                      type="button"
                      onClick={() => session.activateBranch(String(row.runId))}
                      className="ivory-button px-2 py-0.5 text-[10px] rounded"
                    >
                      {i18n.text("Switch")}
                    </button>
                  </div>
                ))}
              </div>
            )}
          </div>
        </div>

        {/* Right: Historical State & Simulation Inspector */}
        <div className="ivory-panel rounded-md p-5 space-y-4">
          <h3 className="font-display text-sm font-bold m-0">
            {i18n.text("HISTORICAL SNAPSHOT INSPECTOR")}
          </h3>
          <pre className="p-4 rounded bg-[#20201c] text-[#faf5eb] font-mono text-xs overflow-auto max-h-[560px] whitespace-pre-wrap m-0">
            {simResult ||
              inspectedState ||
              i18n.text("Select a command sequence on the left to inspect its saved state.")}
          </pre>
        </div>
      </div>
    </div>
  );
};

export const RunHistoryScreen: React.FC<{
  services: AppServices;
  onSelectReplay: (runId: string) => void;
  onNavigate: (screen: ScreenId) => void;
}> = ({ services, onSelectReplay, onNavigate }) => {
  const { session, i18n, audio } = services;
  const [history, setHistory] = useState<JsonObject>({});

  useEffect(() => {
    session.fetchProfileHistory().then(setHistory);
  }, [session]);

  const runs = Array.isArray(history.runs)
    ? (history.runs as JsonObject[])
    : [];

  return (
    <div className="flex flex-col h-full w-full ivory-surface overflow-hidden">
      <header className="flex items-center justify-between px-6 py-3 bg-[#eee4d1] border-b-2 border-[#20201c]">
        <h2 className="font-display text-lg font-bold m-0">
          {i18n.text("EXPEDITION ARCHIVE & RUN HISTORY")}
        </h2>
        <button
          type="button"
          onClick={() => onNavigate("menu")}
          className="ivory-button px-3.5 py-1 text-xs rounded"
        >
          {i18n.text("MAIN MENU")}
        </button>
      </header>

      <div className="flex-1 overflow-y-auto p-6 max-w-5xl w-full mx-auto space-y-4">
        {runs.length === 0 ? (
          <div className="ivory-panel rounded-md p-8 text-center text-sm text-[#5b574f]">
            {i18n.text("No recorded runs found on the connected HeroScript server.")}
          </div>
        ) : (
          runs.map((run, idx) => {
            const runId = String(run.runId ?? idx);
            return (
              <div
                key={runId}
                className="ivory-panel rounded-md p-4 flex flex-wrap items-center justify-between gap-4"
              >
                <div>
                  <div className="font-display font-bold text-sm text-[#20201c]">
                    {String(run.settingId ?? "Run")} · {runId}
                  </div>
                  <div className="text-xs text-[#5b574f]">
                    {i18n.text("Lifecycle")}: {String(run.lifecycle ?? "—")} ·{" "}
                    {i18n.text("Sequence")}: {Number(run.sequence ?? 0)}
                  </div>
                </div>
                <div className="flex items-center gap-2">
                  <button
                    type="button"
                    onClick={() => {
                      audio.ui();
                      onSelectReplay(runId);
                    }}
                    className="ivory-button-primary px-3.5 py-1.5 text-xs rounded"
                  >
                    {i18n.text("INSPECT REPLAY")}
                  </button>
                  <button
                    type="button"
                    onClick={async () => {
                      const ok = await session.resumeRun(runId);
                      if (ok) {
                        onNavigate(
                          session.hasActiveCombat() ? "combat" : "journey"
                        );
                      }
                    }}
                    className="ivory-button px-3.5 py-1.5 text-xs rounded"
                  >
                    {i18n.text("RESUME")}
                  </button>
                </div>
              </div>
            );
          })
        )}
      </div>
    </div>
  );
};

export const RunReplayScreen: React.FC<{
  services: AppServices;
  runId: string;
  onNavigate: (screen: ScreenId) => void;
}> = ({ services, runId, onNavigate }) => {
  const { session, i18n } = services;
  const [, setTick] = useState(0);
  const controllerRef = useRef<RunReplayController | null>(null);

  useEffect(() => {
    const ctrl = new RunReplayController();
    ctrl.setup(
      session,
      runId || String(session.run.runId ?? ""),
      i18n
    );
    controllerRef.current = ctrl;
    const unsub = ctrl.changed.connect(() => setTick((t) => t + 1));
    ctrl.start();
    return () => {
      unsub();
      ctrl.dispose();
    };
  }, [session, runId, i18n]);

  const model = controllerRef.current?.model() ?? {};
  const entries = Array.isArray(model.entries)
    ? (model.entries as JsonObject[])
    : [];
  const selectedIndex = Number(model.selected_index ?? -1);

  return (
    <div className="flex flex-col h-full w-full ivory-surface overflow-hidden">
      <header className="flex items-center justify-between px-6 py-3 bg-[#eee4d1] border-b-2 border-[#20201c]">
        <h2 className="font-display text-lg font-bold m-0">
          {i18n.text("DETERMINISTIC REPLAY")} — {runId || String(session.run.runId ?? "")}
        </h2>
        <div className="flex items-center gap-2">
          <button
            type="button"
            onClick={() => controllerRef.current?.navigate(-1)}
            className="ivory-button px-3 py-1 text-xs rounded"
          >
            ‹ {i18n.text("Prev")}
          </button>
          <button
            type="button"
            onClick={() => controllerRef.current?.navigate(1)}
            className="ivory-button px-3 py-1 text-xs rounded"
          >
            {i18n.text("Next")} ›
          </button>
          <button
            type="button"
            onClick={() => onNavigate("history")}
            className="ivory-button px-3.5 py-1 text-xs rounded"
          >
            {i18n.text("BACK")}
          </button>
        </div>
      </header>

      <div className="flex-1 overflow-y-auto p-6 grid grid-cols-1 lg:grid-cols-[320px_1fr] gap-6 items-start">
        <div className="ivory-panel rounded-md p-4 space-y-2 max-h-[600px] overflow-y-auto">
          {entries.map((entry, idx) => (
            <button
              key={idx}
              type="button"
              onClick={() => controllerRef.current?.select(idx)}
              className={`w-full text-left p-2.5 rounded border text-xs ${
                selectedIndex === idx
                  ? "bg-[#20201c] text-[#faf5eb] border-[#20201c]"
                  : "bg-[#faf5eb] text-[#20201c] border-[#8d8270]/50"
              }`}
            >
              #{Number(entry.sequence ?? idx + 1)} ·{" "}
              {String(entry.commandType ?? "COMMIT")}
            </button>
          ))}
        </div>

        <div className="space-y-4">
          <div className="ivory-panel rounded-md p-5">
            <h3 className="font-display text-sm font-bold m-0 mb-2">
              {i18n.text("COMMIT SUMMARY")}
            </h3>
            <pre className="p-3.5 rounded bg-[#20201c] text-[#faf5eb] font-mono text-xs whitespace-pre-wrap m-0">
              {String(model.state_text ?? "")}
            </pre>
          </div>

          <div className="ivory-panel rounded-md p-5">
            <h3 className="font-display text-sm font-bold m-0 mb-2">
              {i18n.text("RECORDED FRAMES")}
            </h3>
            <pre className="p-3.5 rounded bg-[#20201c] text-[#faf5eb] font-mono text-xs whitespace-pre-wrap m-0">
              {String(model.frame_text ?? "")}
            </pre>
          </div>
        </div>
      </div>
    </div>
  );
};

export const SandboxScreen: React.FC<{
  services: AppServices;
  onNavigate: (screen: ScreenId) => void;
}> = ({ services, onNavigate }) => {
  const { session, i18n, audio } = services;
  const scenariosDoc = tagSynergyScenarios as unknown as JsonObject;
  const scenarios = Array.isArray(scenariosDoc.scenarios)
    ? (scenariosDoc.scenarios as JsonObject[])
    : [];

  const [scenarioIdx, setScenarioIdx] = useState(0);
  const [seed, setSeed] = useState(20260911);
  const [customJson, setCustomJson] = useState(() =>
    JSON.stringify(scenarios[0] ?? { modeId: "sandbox", seed: 20260911 }, null, 2)
  );

  return (
    <div className="flex flex-col h-full w-full ivory-surface overflow-hidden">
      <header className="flex items-center justify-between px-6 py-3 bg-[#eee4d1] border-b-2 border-[#20201c]">
        <h2 className="font-display text-lg font-bold m-0">
          {i18n.text("RULES LAB & SYNERGY SANDBOX")}
        </h2>
        <button
          type="button"
          onClick={() => onNavigate("menu")}
          className="ivory-button px-3.5 py-1 text-xs rounded"
        >
          {i18n.text("MAIN MENU")}
        </button>
      </header>

      <div className="flex-1 overflow-y-auto p-6 max-w-4xl w-full mx-auto space-y-4">
        <div className="ivory-panel rounded-md p-5 space-y-4 text-xs">
          <div className="grid grid-cols-1 sm:grid-cols-2 gap-4">
            {scenarios.length > 0 && (
              <div>
                <label className="block font-display font-bold mb-1">
                  {i18n.text("Synergy Preset")}
                </label>
                <select
                  value={scenarioIdx}
                  onChange={(e) => {
                    const idx = Number(e.target.value);
                    setScenarioIdx(idx);
                    setCustomJson(
                      JSON.stringify(scenarios[idx] ?? {}, null, 2)
                    );
                  }}
                  className="w-full p-2 rounded border border-[#20201c] bg-[#faf5eb]"
                >
                  {scenarios.map((s, i) => (
                    <option key={i} value={i}>
                      {String(s.name ?? s.id ?? `Scenario ${i + 1}`)}
                    </option>
                  ))}
                </select>
              </div>
            )}

            <div>
              <label className="block font-display font-bold mb-1">
                {i18n.text("Deterministic Seed")}
              </label>
              <input
                type="number"
                value={seed}
                onChange={(e) => setSeed(Math.trunc(Number(e.target.value)))}
                className="w-full p-2 rounded border border-[#20201c] bg-[#faf5eb] font-mono"
              />
            </div>
          </div>

          <div>
            <label className="block font-display font-bold mb-1">
              {i18n.text("Run Launch Payload (JSON)")}
            </label>
            <textarea
              rows={12}
              value={customJson}
              onChange={(e) => setCustomJson(e.target.value)}
              className="w-full p-3 rounded bg-[#20201c] text-[#faf5eb] font-mono text-xs"
            />
          </div>

          <div className="flex justify-end">
            <button
              type="button"
              disabled={!session.engineOnline || session.busy}
              onClick={async () => {
                audio.ui();
                try {
                  const parsed = JSON.parse(customJson);
                  if (isPlainObject(parsed)) {
                    parsed.seed = seed;
                    const ok = await session.startCustomRun(parsed);
                    if (ok) {
                      onNavigate(
                        session.hasActiveCombat() ? "combat" : "journey"
                      );
                    }
                  }
                } catch {
                  // Invalid JSON
                }
              }}
              className="ivory-button-primary px-6 py-2.5 text-xs rounded"
            >
              {i18n.text("LAUNCH SANDBOX RUN")}
            </button>
          </div>
        </div>
      </div>
    </div>
  );
};

export const CodexScreen: React.FC<{
  services: AppServices;
  onNavigate: (screen: ScreenId) => void;
}> = ({ services, onNavigate }) => {
  const { session, i18n } = services;
  const categories = [
    "cards",
    "entities",
    "resources",
    "status-effects",
    "relics",
    "modifiers",
    "modes",
    "combat-rules",
    "runs",
  ];
  const [category, setCategory] = useState("cards");
  const [items, setItems] = useState<unknown[]>([]);
  const [selectedIdx, setSelectedIdx] = useState(0);

  useEffect(() => {
    if (!session.engineOnline) return;
    session.fetchCollection(category).then((list) => {
      setItems(list);
      setSelectedIdx(0);
    });
  }, [category, session]);

  return (
    <div className="flex flex-col h-full w-full ivory-surface overflow-hidden">
      <header className="flex items-center justify-between px-6 py-3 bg-[#eee4d1] border-b-2 border-[#20201c]">
        <h2 className="font-display text-lg font-bold m-0">
          {i18n.text("PUBLISHED CONTENT CODEX")}
        </h2>
        <button
          type="button"
          onClick={() => onNavigate("menu")}
          className="ivory-button px-3.5 py-1 text-xs rounded"
        >
          {i18n.text("MAIN MENU")}
        </button>
      </header>

      <div className="flex-1 overflow-y-auto p-6 grid grid-cols-1 lg:grid-cols-[260px_300px_1fr] gap-6 items-start">
        <div className="ivory-panel rounded-md p-3 space-y-1.5">
          {categories.map((cat) => (
            <button
              key={cat}
              type="button"
              onClick={() => setCategory(cat)}
              className={`w-full text-left px-3 py-2 rounded text-xs font-display font-semibold uppercase ${
                category === cat
                  ? "bg-[#20201c] text-[#faf5eb]"
                  : "hover:bg-[#eee4d1]"
              }`}
            >
              {cat}
            </button>
          ))}
        </div>

        <div className="ivory-panel rounded-md p-3 max-h-[600px] overflow-y-auto space-y-1.5">
          {items.length === 0 ? (
            <div className="text-xs text-[#5b574f] p-2 italic">
              {session.engineOnline
                ? i18n.text("No entries in this collection.")
                : i18n.text("Connect to engine to browse published definitions.")}
            </div>
          ) : (
            items.map((item, idx) => {
              const obj = isPlainObject(item) ? item : {};
              const id = String(
                obj.id ?? obj.definitionId ?? obj.modeId ?? idx
              );
              return (
                <button
                  key={id}
                  type="button"
                  onClick={() => setSelectedIdx(idx)}
                  className={`w-full text-left px-3 py-2 rounded text-xs truncate ${
                    selectedIdx === idx
                      ? "bg-[#b83e25] text-[#faf5eb] font-bold"
                      : "hover:bg-[#eee4d1]"
                  }`}
                >
                  {i18n.contentName(id, id)}
                </button>
              );
            })
          )}
        </div>

        <div className="ivory-panel rounded-md p-5">
          <pre className="p-4 rounded bg-[#20201c] text-[#faf5eb] font-mono text-xs overflow-auto max-h-[580px] whitespace-pre-wrap m-0">
            {items[selectedIdx]
              ? JSON.stringify(items[selectedIdx], null, 2)
              : i18n.text("Select an item to inspect its raw published definition.")}
          </pre>
        </div>
      </div>
    </div>
  );
};

export const SettingsScreen: React.FC<{
  services: AppServices;
  onNavigate: (screen: ScreenId) => void;
}> = ({ services, onNavigate }) => {
  const { preferences, i18n, session, audio } = services;
  const [, setTick] = useState(0);
  const [activeTab, setActiveTab] = useState("general");
  const [apiInput, setApiInput] = useState(preferences.apiUrl);
  const [connStatus, setConnStatus] = useState("");
  const [remapTarget, setRemapTarget] = useState<string | null>(null);

  const controller = useMemo(
    () => new SettingsController(preferences, i18n, session),
    [preferences, i18n, session]
  );

  const update = (key: string, val: unknown) => {
    controller.setOption(key, val, true);
    setTick((t) => t + 1);
  };

  useEffect(() => {
    if (!remapTarget) return;
    const onKeyDown = (e: KeyboardEvent) => {
      e.preventDefault();
      const err = controller.remapKey(
        remapTarget,
        e.key,
        e.ctrlKey || e.altKey || e.metaKey || e.shiftKey
      );
      setConnStatus(err);
      setRemapTarget(null);
      setTick((t) => t + 1);
    };
    window.addEventListener("keydown", onKeyDown);
    return () => window.removeEventListener("keydown", onKeyDown);
  }, [remapTarget, controller]);

  return (
    <div className="flex flex-col h-full w-full ivory-surface overflow-hidden">
      <header className="flex items-center justify-between px-6 py-3 bg-[#eee4d1] border-b-2 border-[#20201c]">
        <h2 className="font-display text-lg font-bold m-0">
          {i18n.text("PREFERENCES & SYSTEM CONFIGURATION")}
        </h2>
        <button
          type="button"
          onClick={() => onNavigate("menu")}
          className="ivory-button px-3.5 py-1 text-xs rounded"
        >
          {i18n.text("MAIN MENU")}
        </button>
      </header>

      <div className="flex-1 overflow-y-auto p-6 max-w-5xl w-full mx-auto grid grid-cols-1 md:grid-cols-[240px_1fr] gap-6 items-start">
        {/* Category Tabs */}
        <div className="ivory-panel rounded-md p-3 space-y-1.5">
          {Object.entries(SETTINGS_CATEGORIES).map(([key, [title, desc]]) => (
            <button
              key={key}
              type="button"
              onClick={() => {
                audio.ui();
                setActiveTab(key);
              }}
              className={`w-full text-left p-2.5 rounded transition-colors ${
                activeTab === key
                  ? "bg-[#20201c] text-[#faf5eb]"
                  : "hover:bg-[#eee4d1]"
              }`}
            >
              <div className="font-display text-xs font-bold">
                {i18n.text(title)}
              </div>
              <div className="text-[11px] opacity-80">{i18n.text(desc)}</div>
            </button>
          ))}
        </div>

        {/* Settings Panel */}
        <div className="ivory-panel rounded-md p-6 space-y-5 text-sm">
          {activeTab === "general" && (
            <div className="space-y-4">
              <div>
                <label className="block font-display text-xs font-bold mb-1">
                  {i18n.text("LANGUAGE / LOCALE")}
                </label>
                <select
                  value={preferences.locale}
                  onChange={(e) => update("locale", e.target.value)}
                  className="p-2 rounded border border-[#20201c] bg-[#faf5eb] font-semibold"
                >
                  <option value="en">English (en)</option>
                  <option value="pt_BR">Português — Brasil (pt_BR)</option>
                </select>
              </div>

              <div>
                <label className="block font-display text-xs font-bold mb-1">
                  {i18n.text("HAND LAYOUT MODE")}
                </label>
                <select
                  value={preferences.handLayoutMode}
                  onChange={(e) => update("hand_layout_mode", e.target.value)}
                  className="p-2 rounded border border-[#20201c] bg-[#faf5eb] font-semibold"
                >
                  <option value="adaptive">{i18n.text("Adaptive Fan")}</option>
                  <option value="reading">{i18n.text("Reading Strip")}</option>
                </select>
              </div>

              <div>
                <label className="block font-display text-xs font-bold mb-1">
                  {i18n.text("ANIMATION PRESET")}
                </label>
                <select
                  value={preferences.animationPreset}
                  onChange={(e) => update("animation_preset", e.target.value)}
                  className="p-2 rounded border border-[#20201c] bg-[#faf5eb] font-semibold"
                >
                  <option value="normal">{i18n.text("Normal")}</option>
                  <option value="fast">{i18n.text("Fast (2x)")}</option>
                  <option value="instant">{i18n.text("Instant")}</option>
                </select>
              </div>

              <label className="flex items-center gap-2 cursor-pointer">
                <input
                  type="checkbox"
                  checked={preferences.autoAnimations}
                  onChange={(e) => update("auto_animations", e.target.checked)}
                />
                <span>{i18n.text("Advance combat frames automatically")}</span>
              </label>
            </div>
          )}

          {activeTab === "audio" && (
            <div className="space-y-4">
              {(
                [
                  ["master_volume", "Master Volume", preferences.masterVolume],
                  ["music_volume", "Music Volume", preferences.musicVolume],
                  ["sfx_volume", "SFX Volume", preferences.sfxVolume],
                ] as const
              ).map(([key, label, val]) => (
                <div key={key}>
                  <div className="flex justify-between text-xs font-bold mb-1">
                    <span>{i18n.text(label)}</span>
                    <span>{Math.round(val * 100)}%</span>
                  </div>
                  <input
                    type="range"
                    min={0}
                    max={1}
                    step={0.01}
                    value={val}
                    onChange={(e) => update(key, Number(e.target.value))}
                    className="w-full"
                  />
                </div>
              ))}
            </div>
          )}

          {activeTab === "display" && (
            <div className="space-y-4">
              <div>
                <div className="flex justify-between text-xs font-bold mb-1">
                  <span>{i18n.text("Text Scale")}</span>
                  <span>{Math.round(preferences.textScale * 100)}%</span>
                </div>
                <input
                  type="range"
                  min={0.9}
                  max={1.2}
                  step={0.05}
                  value={preferences.textScale}
                  onChange={(e) => update("text_scale", Number(e.target.value))}
                  className="w-full"
                />
              </div>
              <label className="flex items-center gap-2 cursor-pointer">
                <input
                  type="checkbox"
                  checked={preferences.highContrast}
                  onChange={(e) => update("high_contrast", e.target.checked)}
                />
                <span>{i18n.text("High Contrast Editorial Palette")}</span>
              </label>
            </div>
          )}

          {activeTab === "accessibility" && (
            <div className="space-y-3">
              <label className="flex items-center gap-2 cursor-pointer">
                <input
                  type="checkbox"
                  checked={preferences.reducedMotion}
                  onChange={(e) => update("reduced_motion", e.target.checked)}
                />
                <span>{i18n.text("Reduced Motion")}</span>
              </label>
              <label className="flex items-center gap-2 cursor-pointer">
                <input
                  type="checkbox"
                  checked={preferences.revealTextGradually}
                  onChange={(e) =>
                    update("reveal_text_gradually", e.target.checked)
                  }
                />
                <span>{i18n.text("Reveal dialogue text gradually")}</span>
              </label>
            </div>
          )}

          {activeTab === "controls" && (
            <div className="space-y-3">
              {Preferences.ACTIONS.map((action) => (
                <div
                  key={action}
                  className="flex items-center justify-between py-2 border-b border-[#8d8270]/30"
                >
                  <span className="font-semibold">{i18n.text(action)}</span>
                  <button
                    type="button"
                    onClick={() => setRemapTarget(action)}
                    className="ivory-button px-3 py-1 text-xs rounded font-mono"
                  >
                    {remapTarget === action
                      ? i18n.text("Press key…")
                      : controller.actionLabel(action)}
                  </button>
                </div>
              ))}
              <button
                type="button"
                onClick={() => {
                  controller.resetBindings();
                  setTick((t) => t + 1);
                }}
                className="ivory-button px-3.5 py-1.5 text-xs rounded mt-2"
              >
                {i18n.text("Reset Default Bindings")}
              </button>
            </div>
          )}

          {activeTab === "connection" && (
            <div className="space-y-3">
              <label className="block font-display text-xs font-bold">
                {i18n.text("HEROSCRIPT REST ENDPOINT")}
              </label>
              <input
                type="text"
                value={apiInput}
                onChange={(e) => setApiInput(e.target.value)}
                className="w-full p-2.5 rounded border border-[#20201c] bg-[#faf5eb] font-mono text-xs"
              />
              <div className="flex gap-2">
                <button
                  type="button"
                  onClick={async () => {
                    const err = controller.saveConnection(apiInput);
                    if (err) {
                      setConnStatus(err);
                    } else {
                      setConnStatus(i18n.text("Saved. Reconnecting…"));
                      await session.loadCatalog();
                      setConnStatus(
                        session.engineOnline
                          ? i18n.text("Connected to HeroScript engine.")
                          : i18n.text("Saved URL, but engine is unreachable.")
                      );
                    }
                  }}
                  className="ivory-button-primary px-4 py-2 text-xs rounded"
                >
                  {i18n.text("SAVE & TEST CONNECTION")}
                </button>
              </div>
              {connStatus && (
                <div className="text-xs font-semibold text-[#b83e25]">
                  {connStatus}
                </div>
              )}
            </div>
          )}
        </div>
      </div>
    </div>
  );
};
